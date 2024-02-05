# ai-hackathon-sql-gen (data agent)

## Description

This project uses GPT chat completions to help users find data in SQL Server. It uses the OpenAI API to generate SQL queries based on user input.

> **This was made during a hackathon and does not account in any way for security, ethics, etc. - it is only meant as a technical proof-of-concept!**

## Installation

Prerequisites:
- [Azure OpenAI subscription](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/create-resource?pivots=web-portal)
- SQL Server database

1. Clone this repository
2. Create an appsettings.json file with the following schema and enter your own values (OAI values - see [REST API docs](https://learn.microsoft.com/en-us/azure/ai-services/openai/reference#completions)):
```json
{
    "ConnectionStrings": {
        "default": "SQL_CONNECTION_STRING"
    },
    "oaiDeploymentName": "DEPLOYMENT_ID",
    "oaiKey": "API_KEY",
    "oaiEndpoint": "ENDPOINT_URL"
}
```
3. Install the required packages: `dotnet restore`
4. Run with `dotnet run`