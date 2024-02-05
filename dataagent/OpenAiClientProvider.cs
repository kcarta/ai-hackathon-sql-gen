using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;

namespace SqlDataAgent;

public class OpenAiClientProvider
{
    public static OpenAIClient CreateOpenAIClient(IConfiguration configuration)
    {
        var azureOpenAIKey = configuration["oaiKey"] ?? throw new Exception("OpenAI key not found in configuration");
        var azureOpenAIUrl = configuration["oaiEndpoint"] ?? throw new Exception("OpenAI endpoint not found in configuration");

        var openAIClient = new OpenAIClient(new Uri(azureOpenAIUrl), new AzureKeyCredential(azureOpenAIKey));
        return openAIClient;
    }
}
