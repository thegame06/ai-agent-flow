namespace AgentFlow.Intents.Catalog.Models;

/// <summary>
/// Root model for deserializing base-intents.yaml catalog.
/// Represents the complete intent catalog structure with metadata, categories, and intent definitions.
/// </summary>
public sealed record IntentCatalog
{
    /// <summary>
    /// Version of the catalog schema (e.g., "1.0").
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Catalog metadata (name, description, maintainer, etc.).
    /// </summary>
    public required IntentCatalogMetadata Metadata { get; init; }

    /// <summary>
    /// List of intent categories for organizational purposes.
    /// </summary>
    public required List<IntentCategory> Categories { get; init; }

    /// <summary>
    /// List of all intent definitions in the catalog.
    /// </summary>
    public required List<IntentDefinitionYaml> Intents { get; init; }
}

/// <summary>
/// Catalog metadata information.
/// </summary>
public sealed record IntentCatalogMetadata
{
    /// <summary>
    /// Display name of the catalog (e.g., "AgentFlow Base Intents").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Detailed description of the catalog purpose.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// License information (e.g., "Proprietary", "MIT").
    /// </summary>
    public required string License { get; init; }

    /// <summary>
    /// Maintainer or owner of the catalog (e.g., "AgentFlow Platform Team").
    /// </summary>
    public required string Maintainer { get; init; }
}

/// <summary>
/// Intent category for grouping related intents.
/// Used for UI organization and reporting.
/// </summary>
public sealed record IntentCategory
{
    /// <summary>
    /// Unique identifier for the category (e.g., "verification", "payments").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name of the category (e.g., "Verificación de Identidad").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Description of the category's purpose.
    /// </summary>
    public required string Description { get; init; }
}

/// <summary>
/// Intent definition as defined in YAML file.
/// This is the raw structure before conversion to the domain model.
/// Maps directly to the YAML schema using YamlDotNet conventions.
/// </summary>
public sealed record IntentDefinitionYaml
{
    /// <summary>
    /// Unique identifier for the intent (e.g., "greeting", "loan_application").
    /// Used as primary key and for routing decisions.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Human-readable name of the intent (e.g., "Saludo", "Solicitud de Préstamo").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Detailed description explaining when this intent should be triggered.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Category ID this intent belongs to (references IntentCategory.Id).
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Example phrases that should trigger this intent.
    /// Used for semantic embedding and training.
    /// </summary>
    public required List<string> Examples { get; init; }

    /// <summary>
    /// Alternative keywords/phrases that represent the same intent.
    /// Used for keyword matching fallback.
    /// </summary>
    public required List<string> Synonyms { get; init; }

    /// <summary>
    /// Minimum confidence score required to consider this intent a match.
    /// Range: 0.0 to 1.0 (e.g., 0.85 means 85% confidence required).
    /// </summary>
    public float ConfidenceThreshold { get; init; }

    /// <summary>
    /// Priority for tie-breaking when multiple intents have similar scores.
    /// Higher values = higher priority (e.g., 500 > 300 > 100).
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Optional: Suggested workflow or agent to route this intent to.
    /// Can be null if routing is determined dynamically.
    /// </summary>
    public string? SuggestedWorkflow { get; init; }

    /// <summary>
    /// Optional: Additional metadata as key-value pairs.
    /// Can contain custom fields like urgency, SLA, lead_score, etc.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}
