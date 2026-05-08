using AgentFlow.Api.Workflow;

namespace AgentFlow.Tests.Unit.Workflow;

public sealed class WorkflowSecurityPolicyServiceTests
{
    [Fact]
    public void ValidateDefinition_Allows_AiKycPaymentsActivities()
    {
        var service = new WorkflowSecurityPolicyService();
        var definition = """
        {
          "activities": [
            { "id": "a1", "type": "ai.agent", "timeoutMs": 1000, "retryCount": 0, "retryDelayMs": 0 },
            { "id": "a2", "type": "kyc.document_check", "timeoutMs": 1000, "retryCount": 0, "retryDelayMs": 0 },
            { "id": "a3", "type": "kyc.review_case", "timeoutMs": 1000, "retryCount": 0, "retryDelayMs": 0 },
            { "id": "a4", "type": "payments.create_intent", "timeoutMs": 1000, "retryCount": 0, "retryDelayMs": 0 }
          ]
        }
        """;

        service.ValidateDefinitionOrThrow(definition);
    }

    [Fact]
    public void ValidateDefinition_Rejects_UnknownActivityType()
    {
        var service = new WorkflowSecurityPolicyService();
        var definition = """
        {
          "activities": [
            { "id": "a1", "type": "unknown.activity", "timeoutMs": 1000, "retryCount": 0, "retryDelayMs": 0 }
          ]
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(() => service.ValidateDefinitionOrThrow(definition));
        Assert.Contains("not allowed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
