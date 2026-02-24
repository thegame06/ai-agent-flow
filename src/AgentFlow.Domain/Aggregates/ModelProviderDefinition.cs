using AgentFlow.Domain.Common;
using MongoDB.Bson;

namespace AgentFlow.Domain.Aggregates;

/// <summary>
/// ModelProviderDefinition — Configuración de infraestructura para un LLM.
/// Contiene el endpoint, la API Key (encriptada) y los modelos que soporta.
/// Unicorn Strategy: Multi-provider management (OpenAI, Azure, Anthropic, Ollama).
/// </summary>
public sealed class ModelProviderDefinition : AggregateRoot
{
    public string Name { get; private set; } = string.Empty; // "Azure-OpenAI-Prod"
    public string ProviderType { get; private set; } = string.Empty; // "OpenAI", "AzureOpenAI", "Anthropic"
    public string ApiUrl { get; private set; } = string.Empty;
    public string EncryptedApiKey { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    
    // Modelos disponibles en este proveedor
    private readonly List<ModelCapability> _models = [];
    public IReadOnlyList<ModelCapability> Models => _models.AsReadOnly();

    private ModelProviderDefinition() { }

    public static ModelProviderDefinition Create(
        string tenantId,
        string name,
        string providerType,
        string apiUrl,
        string encryptedApiKey,
        string createdBy)
    {
        return new ModelProviderDefinition
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = tenantId,
            Name = name,
            ProviderType = providerType,
            ApiUrl = apiUrl,
            EncryptedApiKey = encryptedApiKey,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = createdBy
        };
    }

    public void AddModel(string modelId, string displayName, int contextWindow)
    {
        if (!_models.Any(m => m.ModelId == modelId))
        {
            _models.Add(new ModelCapability 
            { 
                ModelId = modelId, 
                DisplayName = displayName, 
                ContextWindow = contextWindow 
            });
            MarkUpdated(UpdatedBy);
        }
    }
}

public sealed record ModelCapability
{
    public required string ModelId { get; init; }
    public required string DisplayName { get; init; }
    public int ContextWindow { get; init; }
    public bool SupportsFunctions { get; init; } = true;
}
