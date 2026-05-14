using AgentFlow.McpServer.Client;

namespace AgentFlow.McpServer;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra el HttpClient hacia AgentFlow.Api con la URL base y el API key interno.
    /// Configurable en appsettings.json bajo "AgentFlowApi".
    /// </summary>
    public static IServiceCollection AddAgentFlowMcpInfrastructure(
        this IServiceCollection services,
        IConfiguration config)
    {
        var baseUrl = config["AgentFlowApi:BaseUrl"]
            ?? throw new InvalidOperationException("AgentFlowApi:BaseUrl is required in configuration.");

        var apiKey = config["AgentFlowApi:InternalApiKey"];

        services.AddHttpClient<AgentFlowApiClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);

            if (!string.IsNullOrWhiteSpace(apiKey))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        });

        return services;
    }
}
