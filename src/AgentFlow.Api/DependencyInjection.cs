using AgentFlow.Abstractions;
using AgentFlow.Extensions;
using AgentFlow.Application.Memory;
using AgentFlow.Core.Engine;
using AgentFlow.Infrastructure.Memory;
using AgentFlow.Infrastructure.Persistence; // ✅ NEW
using AgentFlow.DSL;
using AgentFlow.Policy;
using AgentFlow.Evaluation;
using AgentFlow.ModelRouting;
using AgentFlow.TestRunner;
using AgentFlow.Events;
using AgentFlow.Prompting;
using AgentFlow.Caching.Redis;
using AgentFlow.Domain.Repositories;
using AgentFlow.Infrastructure.Repositories;
using AgentFlow.Observability;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel;
using MongoDB.Driver;
using System.Text;
using System.Security;

namespace AgentFlow.Api;

public static class DependencyInjection
{
    /// <summary>
    /// Registers all AgentFlow services.
    /// </summary>
    public static IServiceCollection AddAgentFlow(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddDslEngine()
            .AddPolicyEngine()
            .AddEvaluationEngine()
            .AddModelRouting()
            .AddTestRunner()
            .AddEventTransport()
            .AddPromptEngine()
            .AddAgentFlowRedis(configuration.GetConnectionString("Redis") ?? "localhost:6379")
            .AddAgentFlowExtensions()
            .AddMongoDB(configuration)
            .AddRepositories()
            .AddSecurity(configuration)
            .AddAgentEngine()
            .AddMemoryServices()
            .AddAgentFlowObservability(configuration["Telemetry:OtlpEndpoint"] ?? "http://localhost:4317");

        return services;
    }

    private static IServiceCollection AddMongoDB(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MongoDB")
            ?? throw new InvalidOperationException("MongoDB connection string not configured.");

        var databaseName = configuration["MongoDB:DatabaseName"] ?? "agentflow";

        services.AddSingleton<IMongoClient>(_ =>
        {
            var settings = MongoClientSettings.FromConnectionString(connectionString);

            // Enable SSL for non-local environments
            if (!connectionString.Contains("localhost"))
            {
                settings.SslSettings = new SslSettings { EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 };
            }

            return new MongoClient(settings);
        });

        services.AddSingleton<IMongoDatabase>(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));

        return services;
    }

    private static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IAgentDefinitionRepository, AgentDefinitionRepository>();
        services.AddScoped<IAgentExecutionRepository, AgentExecutionRepository>();
        services.AddScoped<IPolicyRepository, PolicyRepository>();
        services.AddScoped<ICheckpointStore, MongoCheckpointStore>();
        services.AddScoped<IPromptProfileStore, MongoPromptProfileStore>();
        services.AddScoped<IConversationThreadRepository, MongoConversationThreadRepository>(); // ✅ NEW

        return services;
    }

    private static IServiceCollection AddSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // TenantContextAccessor: SCOPED (per-request). NEVER singleton.
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddScoped<IAgentAuthorizationService, AgentAuthorizationService>();

        // JWT Authentication
        var jwtKey = configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("JWT secret key not configured.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opt =>
            {
                opt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = configuration["Jwt:Audience"],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                opt.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        // Extract and set TenantContext from claims immediately
                        try
                        {
                            var tenantContext = TenantContext.FromClaims(context.Principal!);
                            var accessor = context.HttpContext.RequestServices
                                .GetRequiredService<ITenantContextAccessor>();
                            accessor.Set(tenantContext);
                        }
                        catch (AgentFlow.Security.SecurityException ex)
                        {
                            context.Fail(ex.Message);
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }

    private static IServiceCollection AddAgentEngine(this IServiceCollection services)
    {
        services.AddScoped<IAgentExecutor, AgentExecutionEngine>();
        services.AddScoped<IToolExecutor, ToolExecutorService>();
        services.AddScoped<IToolAuthorizationService, DefaultToolAuthorizationService>();
        services.AddSingleton<IToolSandbox, DefaultToolSandbox>();
        services.AddSingleton<IToolRegistry, ExtensionToolRegistry>();

        // NOTE: Policy Engine + DSL Engine are registered in AddAgentFlow() via
        // .AddPolicyEngine() and .AddDslEngine() — do NOT duplicate here.

        // Brain: Semantic Kernel - required config
        // SK Kernel is registered as Scoped because it can hold per-request state
        services.AddScoped<IAgentBrain>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetRequiredService<ILogger<SemanticKernelBrain>>();

            var provider = config["SemanticKernel:Provider"] ?? "OpenAI";

            Kernel kernel;
            if (provider == "AzureOpenAI")
            {
                kernel = Kernel.CreateBuilder()
                    .AddAzureOpenAIChatCompletion(
                        deploymentName: config["SemanticKernel:AzureOpenAI:DeploymentName"]!,
                        endpoint: config["SemanticKernel:AzureOpenAI:Endpoint"]!,
                        apiKey: config["SemanticKernel:AzureOpenAI:ApiKey"]!)
                    .Build();
            }
            else
            {
                kernel = Kernel.CreateBuilder()
                    .AddOpenAIChatCompletion(
                        modelId: config["SemanticKernel:OpenAI:ModelId"] ?? "gpt-4o",
                        apiKey: config["SemanticKernel:OpenAI:ApiKey"]!)
                    .Build();
            }

            return new SemanticKernelBrain(kernel, logger);
        });

        return services;
    }

    private static IServiceCollection AddMemoryServices(this IServiceCollection services)
    {
        // services.AddSingleton<IWorkingMemory, InMemoryWorkingMemory>(); // Now handled by AddAgentFlowRedis
        services.AddScoped<ILongTermMemory, MongoLongTermMemory>();
        services.AddScoped<IAuditMemory, MongoAuditMemory>();
        services.AddSingleton<IVectorMemory, NullVectorMemory>();

        services.AddScoped<IAgentMemoryService, AgentMemoryService>();

        return services;
    }
}

// -- Placeholder implementations to be replaced in Phase 2 --

public sealed class DefaultToolAuthorizationService : IToolAuthorizationService
{
    private readonly ITenantContextAccessor _tenantContext;
    private readonly ILogger<DefaultToolAuthorizationService> _logger;

    public DefaultToolAuthorizationService(
        ITenantContextAccessor tenantContext,
        ILogger<DefaultToolAuthorizationService> logger)
    {
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public Task<ToolAuthorizationResult> AuthorizeAsync(
        ToolAuthorizationContext context,
        CancellationToken ct = default)
    {
        var tenantCtx = _tenantContext.Current;
        if (tenantCtx is null)
            return Task.FromResult(ToolAuthorizationResult.Deny("No tenant context."));

        var requiredPermission = context.RiskLevel switch
        {
            ToolRiskLevel.Low => AgentFlowPermissions.ToolExecuteLow,
            ToolRiskLevel.Medium => AgentFlowPermissions.ToolExecuteMedium,
            ToolRiskLevel.High => AgentFlowPermissions.ToolExecuteHigh,
            ToolRiskLevel.Critical => AgentFlowPermissions.ToolExecuteCritical,
            _ => AgentFlowPermissions.ToolExecuteLow
        };

        if (!tenantCtx.HasPermission(requiredPermission))
        {
            _logger.LogWarning("Tool authorization denied: missing {Permission}", requiredPermission);
            return Task.FromResult(ToolAuthorizationResult.Deny($"Missing permission: {requiredPermission}"));
        }

        return Task.FromResult(ToolAuthorizationResult.Allow());
    }
}
