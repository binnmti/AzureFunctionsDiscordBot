using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Discord.WebSocket;
using Discord;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace AzureFunctionsDiscordBot;

public static class DiscordBotFunction
{
    private static DiscordSocketClient _client;
    private static readonly string AzureAIEndpoint = Environment.GetEnvironmentVariable("AzureAIEndpoint");
    private static readonly string AzureAPIKey = Environment.GetEnvironmentVariable("AzureAPIKey");
    private static readonly string DiscordBotToken = Environment.GetEnvironmentVariable("DiscordBotToken");
    private static readonly string DiscordClientID = Environment.GetEnvironmentVariable("DiscordClientID");
    private static readonly string DeploymentName = "gpt-4o";

    [FunctionName("DiscordBotFunction")]
    public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
        ILogger log)
    {
        log.LogInformation("C# HTTP trigger function processed a request.");

        string name = req.Query["name"];
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        dynamic data = JsonConvert.DeserializeObject(requestBody);
        name = name ?? data?.name;
        string responseMessage = string.IsNullOrEmpty(name)
            ? "This HTTP triggered function executed successfully. Pass a name in the query string or in the request body for a personalized response."
            : $"Hello, {name}. This HTTP triggered function executed successfully.";

        await RunDiscordBotAsync(log);
        return new OkObjectResult(responseMessage);
    }

    public static async Task RunDiscordBotAsync(ILogger log)
    {
        if (_client != null) return;

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        });
        _client.Log += (msg) =>
        {
            log.LogInformation(msg.ToString());
            return Task.CompletedTask;
        };
        await _client.LoginAsync(TokenType.Bot, DiscordBotToken);
        await _client.StartAsync();
        _client.MessageReceived += HandleMessageAsync;
        log.LogInformation("Discord bot started.");
    }

    private static async Task HandleMessageAsync(SocketMessage message)
    {
        if (message.Author.IsBot) return;
        if (message.Channel.Id != ulong.Parse(DiscordClientID)) return;

        string response = await GetAzureAIResponse(message.Content);
        await message.Channel.SendMessageAsync(response);
    }

    private static async Task<string> GetAzureAIResponse(string input)
    {
        var credential = new AzureKeyCredential(AzureAPIKey);
        var client = new AzureOpenAIClient(new Uri(AzureAIEndpoint), credential);
        var chatClient = client.GetChatClient(DeploymentName);
        try
        {
            var completion = await chatClient.CompleteChatAsync(new SystemChatMessage(""), new UserChatMessage(input));
            Console.WriteLine($"Completion response: {completion.Value}");
            if (completion.Value.Content != null && completion.Value.Content.Count > 0)
            {
                return completion.Value.Content[0].Text;
            }
            else
            {
                Console.WriteLine("Response content is missing or empty.");
                return "No response content available.";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching Azure AI response: {ex.Message}");
            return $"Error occurred: {ex.Message}";
        }
    }
}
