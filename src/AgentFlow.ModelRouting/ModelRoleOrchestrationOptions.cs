namespace AgentFlow.ModelRouting;

public sealed class ModelRoleOrchestrationOptions
{
    public const string SectionName = "ModelRoleOrchestration";

    public Dictionary<string, ModelRolePolicyOptions> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModelRolePolicyOptions
{
    public double? MaxCostPer1KTokensUsd { get; set; }
    public int? MinContextTokens { get; set; }
    public List<string> PreferredProviders { get; set; } = [];
    public bool EnforceCostCeiling { get; set; } = true;
}
