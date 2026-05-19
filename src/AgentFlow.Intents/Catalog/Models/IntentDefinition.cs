namespace AgentFlow.Intents.Catalog.Models;

/// <summary>
/// Domain model representing a fully-loaded intent definition.
/// Used internally by the intent routing system after loading from YAML or database.
/// Immutable and optimized for runtime performance.
/// </summary>
public sealed record IntentDefinition
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
    /// Category this intent belongs to (e.g., "general", "verification", "payments").
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Example phrases that should trigger this intent.
    /// Used for semantic embedding and training.
    /// Read-only collection for immutability.
    /// </summary>
    public required IReadOnlyList<string> Examples { get; init; }

    /// <summary>
    /// Alternative keywords/phrases that represent the same intent.
    /// Used for keyword matching fallback.
    /// Read-only collection for immutability.
    /// </summary>
    public required IReadOnlyList<string> Synonyms { get; init; }

    /// <summary>
    /// Minimum confidence score required to consider this intent a match.
    /// Range: 0.0 to 1.0 (e.g., 0.85 means 85% confidence required).
    /// </summary>
    public required float ConfidenceThreshold { get; init; }

    /// <summary>
    /// Priority for tie-breaking when multiple intents have similar scores.
    /// Higher values = higher priority (e.g., 500 > 300 > 100).
    /// </summary>
    public required int Priority { get; init; }

    /// <summary>
    /// Optional: Suggested workflow or agent to route this intent to.
    /// Can be null if routing is determined dynamically.
    /// </summary>
    public string? SuggestedWorkflow { get; init; }

    /// <summary>
    /// Indicates whether this is a base intent from the catalog (true)
    /// or a custom tenant-specific intent (false).
    /// Base intents are immutable and shared across tenants.
    /// </summary>
    public required bool IsBaseIntent { get; init; }

    /// <summary>
    /// Version number for tracking intent definition changes over time.
    /// Incremented on updates. Used for cache invalidation and auditing.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// Additional metadata as key-value pairs.
    /// Can contain custom fields like urgency, SLA, lead_score, etc.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates an IntentDefinition from a YAML definition.
    /// Converts mutable lists to read-only collections and sets default values.
    /// </summary>
    /// <param name="yaml">The YAML intent definition.</param>
    /// <param name="isBaseIntent">Whether this is a base intent (default: true).</param>
    /// <param name="version">Version number (default: 1).</param>
    /// <returns>A fully-populated IntentDefinition.</returns>
    public static IntentDefinition FromYaml(
        IntentDefinitionYaml yaml,
        bool isBaseIntent = true,
        int version = 1)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        return new IntentDefinition
        {
            Key = yaml.Key,
            Name = yaml.Name,
            Description = yaml.Description,
            Category = yaml.Category,
            Examples = (yaml.Examples?.Count > 0) 
                ? yaml.Examples.AsReadOnly() 
                : Array.Empty<string>(),
            Synonyms = (yaml.Synonyms?.Count > 0) 
                ? yaml.Synonyms.AsReadOnly() 
                : Array.Empty<string>(),
            ConfidenceThreshold = yaml.ConfidenceThreshold,
            Priority = yaml.Priority,
            SuggestedWorkflow = yaml.SuggestedWorkflow,
            IsBaseIntent = isBaseIntent,
            Version = version,
            Metadata = yaml.Metadata?.AsReadOnly()
        };
    }
}
