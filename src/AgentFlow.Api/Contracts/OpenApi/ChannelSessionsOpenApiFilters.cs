using AgentFlow.Api.Controllers;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AgentFlow.Api.Contracts.OpenApi;

public sealed class ChannelSessionsSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(ChannelSessionDto))
        {
            schema.Example = new OpenApiObject
            {
                ["id"] = new OpenApiString("sess_01hx9x2f8m3v"),
                ["channelId"] = new OpenApiString("whatsapp-sales"),
                ["channelType"] = new OpenApiString("WhatsApp"),
                ["identifier"] = new OpenApiString("+50581143874"),
                ["agentId"] = new OpenApiString("router-agent"),
                ["threadId"] = new OpenApiString("thread_01hx9x3a0h5k"),
                ["status"] = new OpenApiString("Active"),
                ["messageCount"] = new OpenApiInteger(3),
                ["createdAt"] = new OpenApiString("2026-06-04T14:10:00Z"),
                ["lastActivityAt"] = new OpenApiString("2026-06-04T14:12:31Z"),
                ["windowOpen"] = new OpenApiBoolean(true),
                ["unreadCount"] = new OpenApiInteger(3),
                ["replyPending"] = new OpenApiBoolean(true),
                ["lastCustomerMessage"] = new OpenApiString("Es que quiero comprar un celular"),
                ["customerKind"] = new OpenApiString("unknown"),
                ["displayName"] = new OpenApiString("Bladimir"),
                ["routingStage"] = new OpenApiString("classified"),
                ["requiresHumanReview"] = new OpenApiBoolean(false),
                ["operationalState"] = new OpenApiString("classified"),
                ["spamReputationStatus"] = new OpenApiString("none"),
                ["spamSignalCount"] = new OpenApiInteger(0)
            };
            return;
        }

        if (context.Type == typeof(SessionSpamReputationDto))
        {
            schema.Example = new OpenApiObject
            {
                ["sessionId"] = new OpenApiString("sess_01hx9x2f8m3v"),
                ["channelId"] = new OpenApiString("whatsapp-sales"),
                ["identifier"] = new OpenApiString("+50581143874"),
                ["status"] = new OpenApiString("suspected"),
                ["signalCount"] = new OpenApiInteger(2),
                ["lastReasonCode"] = new OpenApiString("heuristic_spam"),
                ["updatedAt"] = new OpenApiString("2026-06-04T14:12:31Z")
            };
            return;
        }

        if (context.Type.IsGenericType &&
            context.Type.GetGenericTypeDefinition() == typeof(PagedResponse<>) &&
            context.Type.GetGenericArguments()[0] == typeof(ChannelSessionDto))
        {
            schema.Example = new OpenApiObject
            {
                ["items"] = new OpenApiArray
                {
                    new OpenApiObject
                    {
                        ["id"] = new OpenApiString("sess_01hx9x2f8m3v"),
                        ["channelId"] = new OpenApiString("whatsapp-sales"),
                        ["channelType"] = new OpenApiString("WhatsApp"),
                        ["identifier"] = new OpenApiString("+50581143874"),
                        ["status"] = new OpenApiString("Active"),
                        ["messageCount"] = new OpenApiInteger(2),
                        ["lastActivityAt"] = new OpenApiString("2026-06-04T14:11:12Z"),
                        ["replyPending"] = new OpenApiBoolean(true),
                        ["routingStage"] = new OpenApiString("accumulating"),
                        ["requiresHumanReview"] = new OpenApiBoolean(false),
                        ["operationalState"] = new OpenApiString("awaiting_classification"),
                        ["spamReputationStatus"] = new OpenApiString("none"),
                        ["spamSignalCount"] = new OpenApiInteger(0)
                    }
                },
                ["total"] = new OpenApiLong(1),
                ["page"] = new OpenApiInteger(0),
                ["pageSize"] = new OpenApiInteger(25),
                ["hasMore"] = new OpenApiBoolean(false)
            };
        }
    }
}

public sealed class ChannelSessionsOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(ChannelSessionsController))
            return;

        switch (context.MethodInfo.Name)
        {
            case nameof(ChannelSessionsController.GetActive):
                operation.Summary = "List channel sessions";
                operation.Description = "Returns channel sessions with derived routing state, spam reputation, and human-review flags for inbox and operations clients.";
                DescribeParameter(operation, "operationalState",
                    "Filter by derived routing/inbox state. Allowed values: awaiting_classification, classified, pending_human_review, escalated_human, spam_review.",
                    "awaiting_classification");
                DescribeParameter(operation, "status",
                    "Filter by persisted session lifecycle status. Allowed values depend on SessionStatus: Active, Closed, Paused, Expired.",
                    "Active");
                DescribeParameter(operation, "query",
                    "Case-insensitive match against the customer identifier stored in the session.",
                    "+505");
                break;

            case nameof(ChannelSessionsController.GetById):
                operation.Summary = "Get channel session";
                operation.Description = "Returns one session with routing stage, operational state, embedded spam reputation, and recent session-level evidence fields.";
                break;

            case nameof(ChannelSessionsController.GetSpamReputation):
                operation.Summary = "Get session spam reputation";
                operation.Description = "Returns the persisted spam reputation resolved by tenant, channel, and customer identifier for the given session.";
                break;

            case nameof(ChannelSessionsController.UpdateSpamReputation):
                operation.Summary = "Update session spam reputation";
                operation.Description = "Marks the customer reputation as none, suspected, confirmed_spam, or cleared and updates the session review state accordingly.";
                break;
        }
    }

    private static void DescribeParameter(OpenApiOperation operation, string name, string description, string? example = null)
    {
        var parameter = operation.Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (parameter is null)
            return;

        parameter.Description = description;
        if (!string.IsNullOrWhiteSpace(example))
            parameter.Example = new OpenApiString(example);
    }
}
