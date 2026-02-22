using AgentFlow.Abstractions;
using System.Text;

namespace AgentFlow.Prompting;

// =========================================================================
// PROMPT BLOCKS — Composable building units
// =========================================================================

/// <summary>
/// A composable prompt block. Blocks are assembled into a full prompt.
/// Hierarchy: Platform > Tenant > Agent > Execution
/// Only "Override" blocks at lower levels can replace higher-level definitions.
/// </summary>
public abstract record PromptBlock
{
    public required string BlockId { get; init; }
    public required string BlockType { get; init; }
    public int Order { get; init; }
    public bool IsOverridable { get; init; } = true;
}

public sealed record SystemRoleBlock : PromptBlock
{
    public required string Content { get; init; }
}

public sealed record GuardrailBlock : PromptBlock
{
    public required string Content { get; init; }
    public bool IsNonNegotiable { get; init; } = false;
}

public sealed record CapabilityBlock : PromptBlock
{
    public required string Content { get; init; }
}

public sealed record ContextBlock : PromptBlock  // Dynamic: injected per-execution
{
    public required string Template { get; init; } // Supports {variable} tokens
    public IReadOnlyList<string> RequiredVariables { get; init; } = [];
}

public sealed record ExamplesBlock : PromptBlock
{
    public IReadOnlyList<PromptExample> Examples { get; init; } = [];
}

public sealed record PromptExample
{
    public required string Input { get; init; }
    public required string ExpectedBehavior { get; init; }
}

// =========================================================================
// PROMPT PROFILE
// =========================================================================

/// <summary>
/// Versioned collection of prompt blocks for an agent.
/// Stored in MongoDB. Referenced by AgentDefinition via ProfileId@Version.
/// </summary>
public sealed record PromptProfile
{
    public required string ProfileId { get; init; }
    public required string Version { get; init; }
    public required string TenantId { get; init; }
    public bool IsPublished { get; init; }
    public IReadOnlyList<PromptBlock> Blocks { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

// =========================================================================
// PROMPT RENDERER
// =========================================================================

public interface IPromptRenderer
{
    /// <summary>Assembles a full system prompt from a profile and execution context variables.</summary>
    Task<RenderedPrompt> RenderAsync(
        PromptProfile profile,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken ct = default);
}

public sealed record RenderedPrompt
{
    public required string SystemPrompt { get; init; }
    public int EstimatedTokenCount { get; init; }    // rough estimate (chars/4)
    public IReadOnlyList<string> BlocksIncluded { get; init; } = [];
    public IReadOnlyList<string> MissingVariables { get; init; } = [];
}

public sealed class PromptRenderer : IPromptRenderer
{
    public Task<RenderedPrompt> RenderAsync(
        PromptProfile profile,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var includedBlocks = new List<string>();
        var missingVars = new List<string>();

        var orderedBlocks = profile.Blocks.OrderBy(b => b.Order);

        foreach (var block in orderedBlocks)
        {
            var rendered = block switch
            {
                SystemRoleBlock r  => r.Content,
                GuardrailBlock g   => RenderGuardrail(g),
                CapabilityBlock c  => c.Content,
                ContextBlock ctx   => RenderContext(ctx, variables, missingVars),
                ExamplesBlock ex   => RenderExamples(ex),
                _                  => string.Empty
            };

            if (!string.IsNullOrWhiteSpace(rendered))
            {
                sb.AppendLine(rendered);
                sb.AppendLine();
                includedBlocks.Add(block.BlockId);
            }
        }

        var systemPrompt = sb.ToString().Trim();

        return Task.FromResult(new RenderedPrompt
        {
            SystemPrompt = systemPrompt,
            EstimatedTokenCount = systemPrompt.Length / 4,
            BlocksIncluded = includedBlocks,
            MissingVariables = missingVars
        });
    }

    private static string RenderGuardrail(GuardrailBlock g)
    {
        if (g.IsNonNegotiable)
            return $"[MANDATORY CONSTRAINT — NEVER OVERRIDE]\n{g.Content}";
        return $"[CONSTRAINT]\n{g.Content}";
    }

    private static string RenderContext(
        ContextBlock ctx,
        IReadOnlyDictionary<string, string> variables,
        List<string> missingVars)
    {
        var result = ctx.Template;
        foreach (var required in ctx.RequiredVariables)
        {
            if (variables.TryGetValue(required, out var value))
                result = result.Replace($"{{{required}}}", value);
            else
                missingVars.Add(required);
        }
        return result;
    }

    private static string RenderExamples(ExamplesBlock ex)
    {
        if (ex.Examples.Count == 0) return string.Empty;

        var sb = new StringBuilder("Examples:\n");
        foreach (var e in ex.Examples)
            sb.AppendLine($"- Input: {e.Input}\n  Expected behavior: {e.ExpectedBehavior}");
        return sb.ToString();
    }
}

// =========================================================================
// PROMPT PROFILE STORE (interface — MongoDB impl in Persistence)
// =========================================================================

public interface IPromptProfileStore
{
    Task<PromptProfile?> GetAsync(string profileId, string tenantId, string? version = null, CancellationToken ct = default);
    Task SaveAsync(PromptProfile profile, CancellationToken ct = default);
    Task<IReadOnlyList<PromptProfile>> ListPublishedAsync(string tenantId, CancellationToken ct = default);
}

// =========================================================================
// IN-MEMORY PROFILE STORE (for tests/bootstrap)
// =========================================================================

public sealed class InMemoryPromptProfileStore : IPromptProfileStore
{
    private readonly Dictionary<string, PromptProfile> _store = new();

    public void Register(PromptProfile profile) =>
        _store[$"{profile.TenantId}:{profile.ProfileId}:{profile.Version}"] = profile;

    public Task<PromptProfile?> GetAsync(string profileId, string tenantId, string? version = null, CancellationToken ct = default)
    {
        if (version is not null)
            return Task.FromResult(_store.GetValueOrDefault($"{tenantId}:{profileId}:{version}"));

        // Get latest published
        var latest = _store.Values
            .Where(p => p.TenantId == tenantId && p.ProfileId == profileId && p.IsPublished)
            .OrderByDescending(p => p.Version)
            .FirstOrDefault();

        return Task.FromResult(latest);
    }

    public Task SaveAsync(PromptProfile profile, CancellationToken ct = default)
    {
        _store[$"{profile.TenantId}:{profile.ProfileId}:{profile.Version}"] = profile;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PromptProfile>> ListPublishedAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PromptProfile>>(
            _store.Values.Where(p => p.TenantId == tenantId && p.IsPublished).ToList());
}
