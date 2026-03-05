using AgentFlow.Abstractions;
using AgentFlow.Api.AuthProfiles;
using AgentFlow.Application.Channels;
using AgentFlow.Application.Memory;
using AgentFlow.Caching.Redis;
using AgentFlow.Core.Engine;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.DSL;
using AgentFlow.Evaluation;
using AgentFlow.Events;
using AgentFlow.Extensions;
using AgentFlow.Infrastructure.Channels.Api;
using AgentFlow.Infrastructure.Channels.WebChat;
using AgentFlow.Infrastructure.Channels.WhatsApp;
using AgentFlow.Infrastructure.Memory;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Repositories;
using AgentFlow.ModelRouting;
using AgentFlow.Observability;
using AgentFlow.Policy;
using AgentFlow.Prompting;
using AgentFlow.Security;
using AgentFlow.TestRunner;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System.Text;

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
            .AddChannelGateway(configuration)
            .AddSingleton<IAuthProfilesStore, InMemoryAuthProfilesStore>()
            .AddSecurity(configuration)
            .AddAgentEngine(configuration)
            .AddMemoryServices()
            .AddAgentFlowObservability(configuration["Telemetry:OtlpEndpoint"] ?? "http://localhost:4317")
            .AddMcpGateway(configuration);

        return services;
    }

    private static IServiceCollection AddMongoDB(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MongoDB")
            ?? throw new InvalidOperationException("MongoDB connection string not configured.");

        var databaseName = configuration["MongoDB:DatabaseName"] ?? "agentflow";

        // ✅ CRITICAL FIX: Configure BsonClassMap for ConversationThread to serialize private fields
        if (!BsonClassMap.IsClassMapRegistered(typeof(ConversationThread)))
        {
            BsonClassMap.RegisterClassMap<ConversationThread>(cm =>
            {
                cm.AutoMap();
                
                // Ignore computed property TurnCount (it's derived from _executionIds.Count)
                cm.UnmapProperty(nameof(ConversationThread.TurnCount));
                
                // Ignore readonly wrapper ExecutionIds (we'll serialize _executionIds directly)
                cm.UnmapProperty(nameof(ConversationThread.ExecutionIds));
                
                // Map private field _executionIds to MongoDB using its natural field name
                cm.MapField("_executionIds")
                  .SetElementName("_executionIds");  // Use the actual field name with underscore
            });
        }

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
        services.AddScoped<IConversationThreadRepository, MongoConversationThreadRepository>();
        services.AddChannelRepositories();

        return services;
    }

    private static IServiceCollection AddChannelGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IChannelGateway, ChannelGateway>();
        services.AddSingleton<IChannelHandler, WhatsAppChannelHandler>();
        services.AddSingleton<IChannelHandler, WebChatChannelHandler>();
        services.AddSingleton<IChannelHandler, ApiChannelHandler>();
        services.Configure<WhatsAppOptions>(configuration.GetSection("WhatsApp"));

        return services;
    }

    private static IServiceCollection AddChannelRepositories(this IServiceCollection services)
    {
        services.AddSingleton<IChannelDefinitionRepository, MongoChannelDefinitionRepository>();
        services.AddSingleton<IChannelSessionRepository, MongoChannelSessionRepository>();
        services.AddSingleton<IChannelMessageRepository, MongoChannelMessageRepository>();

        return services;
    }

    private static IServiceCollection AddSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // TenantContextAccessor: SCOPED (per-request). NEVER singleton.
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddScoped<IAgentAuthorizationService, AgentAuthorizationService>();
        services.AddSingleton<ConfigurationManagerHandoffPolicy>();
        services.AddScoped<IIntentRoutingStore, AgentFlow.Infrastructure.Repositories.MongoIntentRoutingStore>();
        services.AddScoped<IManagerHandoffPolicy, PersistentManagerHandoffPolicy>();

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

    private static IServiceCollection AddAgentEngine(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAgentExecutor, AgentExecutionEngine>();
        services.AddScoped<IAgentHandoffExecutor, AgentHandoffExecutor>();
        services.AddScoped<IToolExecutor, ToolExecutorService>();
        services.AddScoped<IToolAuthorizationService, DefaultToolAuthorizationService>();
        services.AddSingleton<IToolSandbox, DefaultToolSandbox>();
        services.AddSingleton<IToolRegistry, ExtensionToolRegistry>();

        // Register Brains
        services.AddAgentBrains(configuration);

        return services;
    }

    private static IServiceCollection AddAgentBrains(this IServiceCollection services, IConfiguration configuration)
    {
        var defaultProviderStr = configuration["AgentBrain:DefaultProvider"] ?? "SemanticKernel";
        if (!Enum.TryParse<BrainProvider>(defaultProviderStr, true, out var defaultProvider))
        {
            defaultProvider = BrainProvider.SemanticKernel;
        }

        // Register all implementations
        services.AddScoped<SemanticKernelBrain>();
        services.AddScoped<MafBrain>();

        // Resolver for IAgentBrain
        services.AddScoped<IAgentBrain>(sp =>
        {
            // Future: This could resolve based on AgentKey or TenantId from the request
            return defaultProvider switch
            {
                BrainProvider.MicrosoftAgentFramework => sp.GetRequiredService<MafBrain>(),
                _ => sp.GetRequiredService<SemanticKernelBrain>()
            };
        });

        // Specific configuration for Semantic Kernel
        services.AddScoped<Kernel>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var provider = config["SemanticKernel:Provider"] ?? "OpenAI";

            if (provider == "AzureOpenAI")
            {
                return Kernel.CreateBuilder()
                    .AddAzureOpenAIChatCompletion(
                        deploymentName: config["SemanticKernel:AzureOpenAI:DeploymentName"]!,
                        endpoint: config["SemanticKernel:AzureOpenAI:Endpoint"]!,
                        apiKey: config["SemanticKernel:AzureOpenAI:ApiKey"]!)
                    .Build();
            }
            
            return Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(
                    modelId: config["SemanticKernel:OpenAI:ModelId"] ?? "gpt-4o",
                    apiKey: config["SemanticKernel:OpenAI:ApiKey"]!)
                .Build();
        });

        return services;
    }

    private static IServiceCollection AddMcpGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IMcpToolGateway, AgentFlow.Infrastructure.Gateways.McpToolGateway>();
        services.AddHostedService<AgentFlow.Infrastructure.Gateways.McpDiscoveryService>();
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

// -- Baseline implementations to be replaced in Phase 2 --

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
