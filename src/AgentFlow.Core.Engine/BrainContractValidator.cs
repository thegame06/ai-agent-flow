using AgentFlow.Abstractions;
using System.Text.Json;

namespace AgentFlow.Core.Engine;

internal static class BrainContractValidator
{
    internal static ThinkResult NormalizeThinkResult(ThinkResult candidate, string brainName)
    {
        candidate = CoerceCommonThinkResultVariants(candidate);

        var errors = ValidateThinkResult(candidate);
        if (errors.Count == 0)
            return candidate;

        return new ThinkResult
        {
            Decision = ThinkDecision.ProvideFinalAnswer,
            Rationale = SerializeContractErrors(brainName, "ThinkResult", errors),
            FinalAnswer = SerializeContractErrors(brainName, "ThinkResult", errors),
            TokensUsed = candidate.TokensUsed
        };
    }

    private static ThinkResult CoerceCommonThinkResultVariants(ThinkResult candidate)
    {
        if (candidate.Decision == ThinkDecision.RequestMoreContext &&
            !string.IsNullOrWhiteSpace(candidate.FinalAnswer) &&
            string.IsNullOrWhiteSpace(candidate.NextToolName) &&
            string.IsNullOrWhiteSpace(candidate.NextToolInputJson))
        {
            return candidate with
            {
                Decision = ThinkDecision.ProvideFinalAnswer
            };
        }

        if (candidate.Decision == ThinkDecision.ProvideFinalAnswer &&
            string.IsNullOrWhiteSpace(candidate.FinalAnswer) &&
            !string.IsNullOrWhiteSpace(candidate.Rationale))
        {
            return candidate with
            {
                FinalAnswer = candidate.Rationale
            };
        }

        if (candidate.Decision == ThinkDecision.UseTool &&
            !string.IsNullOrWhiteSpace(candidate.NextToolName) &&
            !string.IsNullOrWhiteSpace(candidate.NextToolInputJson) &&
            !string.IsNullOrWhiteSpace(candidate.FinalAnswer))
        {
            return candidate with
            {
                FinalAnswer = null
            };
        }

        return candidate;
    }

    internal static ObserveResult NormalizeObserveResult(ObserveResult candidate, string brainName)
    {
        var errors = ValidateObserveResult(candidate);
        if (errors.Count == 0)
            return candidate;

        return new ObserveResult
        {
            Summary = SerializeContractErrors(brainName, "ObserveResult", errors),
            GoalAchieved = false,
            TokensUsed = candidate.TokensUsed
        };
    }

    private static List<string> ValidateThinkResult(ThinkResult candidate)
    {
        var errors = new List<string>();

        switch (candidate.Decision)
        {
            case ThinkDecision.UseTool:
                if (string.IsNullOrWhiteSpace(candidate.NextToolName))
                    errors.Add("UseTool requires nextToolName.");
                if (string.IsNullOrWhiteSpace(candidate.NextToolInputJson))
                    errors.Add("UseTool requires nextToolInputJson.");
                if (!string.IsNullOrWhiteSpace(candidate.FinalAnswer))
                    errors.Add("UseTool forbids finalAnswer.");
                break;

            case ThinkDecision.ProvideFinalAnswer:
                if (string.IsNullOrWhiteSpace(candidate.FinalAnswer))
                    errors.Add("ProvideFinalAnswer requires finalAnswer.");
                if (!string.IsNullOrWhiteSpace(candidate.NextToolName))
                    errors.Add("ProvideFinalAnswer forbids nextToolName.");
                if (!string.IsNullOrWhiteSpace(candidate.NextToolInputJson))
                    errors.Add("ProvideFinalAnswer forbids nextToolInputJson.");
                break;

            case ThinkDecision.RequestMoreContext:
            case ThinkDecision.Checkpoint:
                if (!string.IsNullOrWhiteSpace(candidate.NextToolName))
                    errors.Add($"{candidate.Decision} forbids nextToolName.");
                if (!string.IsNullOrWhiteSpace(candidate.NextToolInputJson))
                    errors.Add($"{candidate.Decision} forbids nextToolInputJson.");
                if (!string.IsNullOrWhiteSpace(candidate.FinalAnswer))
                    errors.Add($"{candidate.Decision} forbids finalAnswer.");
                break;
        }

        return errors;
    }

    private static List<string> ValidateObserveResult(ObserveResult candidate)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(candidate.Summary))
            errors.Add("ObserveResult.summary is required.");
        return errors;
    }

    internal static string SerializeContractErrors(string brainName, string contractType, IReadOnlyList<string> errors)
    {
        var payload = new
        {
            code = "BRAIN_CONTRACT_VIOLATION",
            brain = brainName,
            contractType,
            errors
        };

        return JsonSerializer.Serialize(payload);
    }
}
