using Azure.AI.OpenAI;
using DataAgent;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace SqlDataAgent;

internal class Program
{
    private static void Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // Prep the sql agent prompt
        var sqlAgentSystemMessage = GenerateSqlAgentSystemMessage();
        var querySqlOptions = new ChatCompletionsOptions(config["oaiDeploymentName"], sqlAgentSystemMessage) 
        { 
            Temperature = 0, 
            MaxTokens = 800, 
            PresencePenalty = 0, 
            FrequencyPenalty = 0, 
        };

        // Prep the chat agent prompt
        var chatAgentSystemMessage = GenerateChatAgentSystemMessage();

        var toolDefinition = GenerateToolDefinitionForSql();
        var chatCompletionsOptions = new ChatCompletionsOptions(config["oaiDeploymentName"], chatAgentSystemMessage) 
        { 
            Temperature = 0, 
            MaxTokens = 2000, 
            PresencePenalty = 0, 
            FrequencyPenalty = 0, 
        };
        toolDefinition.ToList().ForEach(chatCompletionsOptions.Tools.Add);

        // agent 1: talk with the user and get the SQL query
        var openAIClient = OpenAiClientProvider.CreateOpenAIClient(config);
        // agent 2: perform the SQL query and get the response
        // TODO: Are two agents needed? Is this split optimal? Originally agent 1 was supposed to get a "semantic query" and agent 2 was supposed to transform that into SQL
        var queryAgent = OpenAiClientProvider.CreateOpenAIClient(config);
        while (true)
        {
            var response = openAIClient.GetChatCompletions(chatCompletionsOptions);

            // Handle Response
            ChatChoice responseChoice = response.Value.Choices[0];
            // If tool call, then we use it to make a SQL query
            if (responseChoice.FinishReason == CompletionsFinishReason.ToolCalls)
            {

                // Perform SQL query and get the response back
                var toolCallResolutionMessages = responseChoice
                    .Message
                    .ToolCalls
                    .Select(toolCall =>
                            GetToolCallResponseMessage(toolCall, querySqlOptions, queryAgent) // side effect: performs SQL query
                    ); 

                // Get the original message
                var toolCallHistoryMessage = new ChatRequestAssistantMessage(responseChoice.Message.Content);
                foreach (ChatCompletionsToolCall requestedToolCall in responseChoice.Message.ToolCalls)
                {
                    // and add the tool calls to the history message
                    toolCallHistoryMessage.ToolCalls.Add(requestedToolCall);
                }

                // Now add the history message and the resolution messages to the chat completions options
                chatCompletionsOptions.Messages.Add(toolCallHistoryMessage);
                toolCallResolutionMessages.ToList().ForEach(chatCompletionsOptions.Messages.Add);
            }
            else // if a regular response, we print it and ask for the next user input
            {
                // Save the response
                var assistantResponse = new ChatRequestAssistantMessage(responseChoice.Message.Content);
                chatCompletionsOptions.Messages.Add(assistantResponse);

                Console.WriteLine("Assistant: " + responseChoice.Message.Content + "\n");

                // Next user input
                Console.Write("User: ");
                var nextUserMessage = new ChatRequestUserMessage(Console.ReadLine());
                Console.WriteLine();
                chatCompletionsOptions.Messages.Add(nextUserMessage);
            }
        }

        ChatRequestSystemMessage[] GenerateSqlAgentSystemMessage()
        {
            /*
            var prompt = """
                Given the input, output a valid T-SQL query that can be directly executed in SQL Server.
                Only output a valid query that can be directly executed, and nothing else.
                Example output: 
                SELECT t.ID FROM table_name t;
            """;
            */

            // TODO this is not working well, the AI goes rogue way too often.
            var prompt = """
                Output a valid T-SQL query. 
                If the input is already valid, just output it back with no other text or formatting. 
                Your output will be immediately executed in SQL Server so it must not contain anything besides a valid query.
            """;

            return [
                new ChatRequestSystemMessage(prompt)
            ];
        }

        ChatRequestMessage[] GenerateChatAgentSystemMessage()
        {
            var prompt = """
                    Your job is to help the user find data in Microsoft SQL Server using the configured tools.
                    When the user asks for general information or insights, you should answer this by producing and running a T-SQL query. 
                    Do not provide general guidance if you can instead make a query.
                """;

            // TODO get the AI to query the information_schema as needed instead of doing it programmatically here 
            var query = """
                SELECT t.TABLE_NAME, c.COLUMN_NAME 
                FROM INFORMATION_SCHEMA.TABLES t
                    INNER JOIN INFORMATION_SCHEMA.COLUMNS c ON t.TABLE_NAME = c.TABLE_NAME
                WHERE t.TABLE_TYPE = 'BASE TABLE' 
                AND t.TABLE_NAME NOT LIKE 'spt_%'
                AND t.TABLE_NAME NOT LIKE 'MSreplication_%'
                ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION;
            """;

            var queryResult = MSSqlService.Search(query, config.GetConnectionString("default") ?? throw new Exception("No connection string found"));
            var csv = CsvFormatter.DataTableToCsv(queryResult);

            prompt += $"\nHere is the database schema with tables and columns: \n{csv}";
            return [
                new ChatRequestSystemMessage(prompt),
                // Seed the conversation with a message from the user
                // Otherwise the agent can hallucinate and start querying the database without any input
                new ChatRequestUserMessage("Hi")
            ];
        }

        List<ChatCompletionsToolDefinition> GenerateToolDefinitionForSql()
        {
            return [
                new ChatCompletionsFunctionToolDefinition()
        {
            Name = "generate_sql_query",
            Description = "Generate T-SQL query based on input from user",
            Parameters = BinaryData.FromObjectAsJson(
                new
                {
                    Type = "object",
                    Properties = new
                    {
                        SemanticRequest = new
                        {
                            Type = "string",
                            Description = "Semantic query that will be converted to T-SQL query"
                        }
                    },
                    Required = new[] { "semanticRequest" },
                },
                new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
        }
            ];
        }

        ChatRequestToolMessage GetToolCallResponseMessage(ChatCompletionsToolCall toolCall, ChatCompletionsOptions sqlQueryOptions, OpenAIClient agent)
        {
            if (toolCall is ChatCompletionsFunctionToolCall functionToolCall && functionToolCall.Name == "generate_sql_query")
            {
                string unvalidatedArguments = functionToolCall.Arguments;
                var query = JsonSerializer.Deserialize<QueryWrapper>(unvalidatedArguments) ?? throw new Exception("Invalid arguments");
                sqlQueryOptions.Messages.Add(new ChatRequestUserMessage(query.semanticRequest));

                var sqlQueryResponse = agent.GetChatCompletions(sqlQueryOptions);
                var sqlQuery = sqlQueryResponse.Value.Choices[0].Message.Content;

                var queryResult = MSSqlService.Search(sqlQuery, config.GetConnectionString("default") ?? throw new Exception("No connection string found"));
                var resultText = CsvFormatter.DataTableToCsv(queryResult);

                return new ChatRequestToolMessage(resultText, toolCall.Id);
            }

            throw new NotImplementedException();
        }
    }
}