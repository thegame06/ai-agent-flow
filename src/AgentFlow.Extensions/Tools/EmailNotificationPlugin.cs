using AgentFlow.ToolSDK;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentFlow.Extensions.Tools;

/// <summary>
/// Mock Email Notification Plugin for Loan Officer Demo.
/// Sends email notifications about loan decisions.
/// 
/// In production, this would integrate with SendGrid, AWS SES, or similar email service.
/// </summary>
public sealed class EmailNotificationPlugin : IToolPlugin
{
    private readonly ILogger<EmailNotificationPlugin> _logger;
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public EmailNotificationPlugin(ILogger<EmailNotificationPlugin> logger)
    {
        _logger = logger;
    }

    public ToolMetadata Metadata => new()
    {
        Id = "email-notification",
        Name = "Email Notification Service",
        Version = "1.0.0",
        Author = "AgentFlow Demo Team",
        Description = "Send email notifications (mock implementation with logging)",
        Tags = new[] { "notification", "email", "communication", "demo" },
        RiskLevel = ToolRiskLevel.Low, // Just sending emails
        License = "MIT",
        DocumentationUrl = "https://docs.agentflow.io/tools/email-notification"
    };

    public ToolSchema GetSchema()
    {
        return new ToolSchema
        {
            Parameters = new Dictionary<string, ParameterSchema>
            {
                ["to"] = new ParameterSchema
                {
                    Type = "string",
                    Description = "Recipient email address",
                    Pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$"
                },
                ["subject"] = new ParameterSchema
                {
                    Type = "string",
                    Description = "Email subject line"
                },
                ["body"] = new ParameterSchema
                {
                    Type = "string",
                    Description = "Email body content (plain text or HTML)"
                },
                ["isHtml"] = new ParameterSchema
                {
                    Type = "boolean",
                    Description = "Whether the body content is HTML",
                    DefaultValue = false
                },
                ["cc"] = new ParameterSchema
                {
                    Type = "array",
                    Description = "CC recipients (optional)",
                    Items = new ParameterSchema { Type = "string" }
                },
                ["priority"] = new ParameterSchema
                {
                    Type = "string",
                    Description = "Email priority level",
                    EnumValues = new[] { "low", "normal", "high" },
                    DefaultValue = "normal"
                }
            },
            Required = new[] { "to", "subject", "body" },
            Example = new
            {
                to = "applicant@example.com",
                subject = "Your Loan Application Status",
                body = "Dear John, your loan application has been approved pending manual review.",
                isHtml = false,
                priority = "high"
            }
        };
    }

    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Email notification requested for tenant {TenantId}, execution {ExecutionId}",
            context.TenantId, context.ExecutionId);

        try
        {
            // Extract parameters
            if (!context.Parameters.TryGetValue("to", out var toObj) || toObj is not string to)
            {
                return ToolResult.FromError(
                    "Parameter 'to' is required and must be a string",
                    "EMAIL_MISSING_RECIPIENT");
            }

            if (!EmailRegex.IsMatch(to))
            {
                return ToolResult.FromError(
                    $"Invalid email address format: {to}",
                    "EMAIL_INVALID_ADDRESS");
            }

            if (!context.Parameters.TryGetValue("subject", out var subjectObj) || subjectObj is not string subject)
            {
                return ToolResult.FromError(
                    "Parameter 'subject' is required and must be a string",
                    "EMAIL_MISSING_SUBJECT");
            }

            if (!context.Parameters.TryGetValue("body", out var bodyObj) || bodyObj is not string body)
            {
                return ToolResult.FromError(
                    "Parameter 'body' is required and must be a string",
                    "EMAIL_MISSING_BODY");
            }

            var isHtml = context.Parameters.TryGetValue("isHtml", out var htmlObj) && htmlObj is bool html && html;
            var priority = context.Parameters.TryGetValue("priority", out var priorityObj) && priorityObj is string p
                ? p
                : "normal";

            List<string> ccList = new();
            if (context.Parameters.TryGetValue("cc", out var ccObj) && ccObj is IEnumerable<object> ccEnum)
            {
                foreach (var email in ccEnum)
                {
                    if (email is string emailStr && EmailRegex.IsMatch(emailStr))
                    {
                        ccList.Add(emailStr);
                    }
                }
            }

            // Simulate email sending latency
            await Task.Delay(Random.Shared.Next(200, 500), ct);

            // In production, this would call SendGrid:
            // var msg = new SendGridMessage
            // {
            //     From = new EmailAddress("noreply@acmebank.com", "Acme Bank"),
            //     Subject = subject,
            //     PlainTextContent = isHtml ? null : body,
            //     HtmlContent = isHtml ? body : null
            // };
            // msg.AddTo(to);
            // var response = await _sendGridClient.SendEmailAsync(msg, ct);

            // For demo, just log the email
            _logger.LogWarning(
                "📧 EMAIL SENT (MOCK)\n" +
                "  To: {To}\n" +
                "  CC: {Cc}\n" +
                "  Subject: {Subject}\n" +
                "  Body (first 100 chars): {BodyPreview}\n" +
                "  HTML: {IsHtml}\n" +
                "  Priority: {Priority}",
                to,
                ccList.Count > 0 ? string.Join(", ", ccList) : "none",
                subject,
                body.Length > 100 ? body.Substring(0, 100) + "..." : body,
                isHtml,
                priority);

            string messageId = $"msg-{Guid.NewGuid():N}";

            var result = new
            {
                success = true,
                messageId,
                recipient = to,
                cc = ccList.ToArray(),
                subject,
                sentAt = DateTimeOffset.UtcNow.ToString("O"),
                provider = "MockEmailService",
                status = "sent",
                deliveryStatus = "pending",
                estimatedDeliverySeconds = 5
            };

            _logger.LogInformation(
                "Email notification sent successfully. MessageId: {MessageId}, Recipient: {Recipient}",
                messageId, to);

            return ToolResult.FromSuccess(JsonSerializer.Serialize(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email notification failed");
            return ToolResult.FromError(
                $"Email sending error: {ex.Message}",
                "EMAIL_SEND_ERROR");
        }
    }

    public PluginCapabilities Capabilities => new()
    {
        SupportsAsync = true,
        SupportsStreaming = false,
        IsCacheable = false, // Don't cache email sends
        RequiresNetwork = true,
        IsReadOnly = false // Sends emails (write operation)
    };

    public IReadOnlyList<PolicyRequirement> RequiredPolicies => new[]
    {
        new PolicyRequirement { PolicyGroupId = "external-communication", Reason = "Tool sends external communications" },
        new PolicyRequirement { PolicyGroupId = "customer-contact-authorized", Reason = "Requires authorization to contact customers" }
    };
}
