using System.Security.Claims;
using AgentFlow.Abstractions;
using AgentFlow.Application.Channels;
using AgentFlow.Api.TestStudio;
using AgentFlow.Api.Workflow;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/test-studio/{runtimeKind}")]
[Authorize]
public sealed class TestStudioController : ControllerBase
{
    private const int MaxMessagesPerMinutePerSession = 30;
    private static readonly HashSet<string> SupportedAttachmentContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp",
        "audio/mpeg",
        "audio/wav",
        "video/mp4",
        "application/pdf",
        "text/plain"
    };

    private readonly ITenantContextAccessor _tenantContext;
    private readonly ITestStudioSessionStore _store;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentExecutor _executor;
    private readonly IConversationThreadRepository _threadRepo;
    private readonly IChannelDefinitionRepository _channelRepo;
    private readonly IChannelGateway _channelGateway;

    public TestStudioController(
        ITenantContextAccessor tenantContext,
        ITestStudioSessionStore store,
        IAgentDefinitionRepository agentRepo,
        IAgentExecutor executor,
        IConversationThreadRepository threadRepo,
        IChannelDefinitionRepository channelRepo,
        IChannelGateway channelGateway)
    {
        _tenantContext = tenantContext;
        _store = store;
        _agentRepo = agentRepo;
        _executor = executor;
        _threadRepo = threadRepo;
        _channelRepo = channelRepo;
        _channelGateway = channelGateway;
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession(
        [FromRoute] string tenantId,
        [FromRoute] string runtimeKind,
        [FromBody] CreateTestStudioSessionRequest request,
        CancellationToken ct)
    {
        if (!TryValidateTenant(tenantId, out var forbidden)) return forbidden!;
        if (!RuntimeCompatibilityPolicy.TryParseRuntimeKind(runtimeKind, out var kind, out _))
            return BadRequest(new { errorCode = TestStudioErrorCatalog.RuntimeIncompatible, message = "Invalid runtime kind." });

        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var session = _store.Create(tenantId, kind, correlationId, request.Mode ?? "direct", request.ChannelType);
        session.AgentId = request.AgentId;
        session.ChannelId = request.ChannelId;

        if (kind == AgentRuntimeKind.Text && string.Equals(session.Mode, "thread", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.AgentId))
                return BadRequest(new { errorCode = TestStudioErrorCatalog.AgentRequired, message = "agentId is required for thread mode." });

            var agent = await _agentRepo.GetByIdAsync(request.AgentId, tenantId, ct);
            if (agent is null) return NotFound(new { errorCode = TestStudioErrorCatalog.AgentNotFound, message = "Agent not found." });
            if (agent.Session.RuntimeKind != AgentRuntimeKind.Text)
                return BadRequest(new { errorCode = TestStudioErrorCatalog.RuntimeIncompatible, message = "Agent runtime is not Text." });

            var userId = ResolveUserId();
            var thread = ConversationThread.Create(
                tenantId,
                $"teststudio:{request.AgentId}:{userId}",
                request.AgentId,
                userId,
                expiresIn: TimeSpan.FromHours(24),
                maxTurns: 200);
            var insert = await _threadRepo.InsertAsync(thread, ct);
            if (!insert.IsSuccess) return BadRequest(new { errorCode = TestStudioErrorCatalog.ThreadCreateFailed, message = insert.Error });
            session.ThreadId = thread.Id;
        }

        _store.AppendEvent(tenantId, session.TestSessionId, new TestStudioEvent
        {
            Stage = "session",
            Direction = "system",
            PayloadType = "session",
            Status = "created",
            CorrelationId = correlationId,
            Message = $"Session created for runtime {kind}."
        });

        return Ok(ToResponse(session, tenantId));
    }

    [HttpGet("sessions")]
    public IActionResult ListSessions(
        [FromRoute] string tenantId,
        [FromRoute] string runtimeKind,
        [FromQuery] string? status = null,
        [FromQuery] string? correlationId = null)
    {
        if (!TryValidateTenant(tenantId, out var forbidden)) return forbidden!;
        if (!RuntimeCompatibilityPolicy.TryParseRuntimeKind(runtimeKind, out var kind, out _))
            return BadRequest(new { errorCode = TestStudioErrorCatalog.RuntimeIncompatible, message = "Invalid runtime kind." });

        var sessions = _store.ListByRuntime(tenantId, kind);
        if (!string.IsNullOrWhiteSpace(status))
            sessions = sessions.Where(s => string.Equals(s.Status, status, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(correlationId))
            sessions = sessions.Where(s => s.CorrelationId.Contains(correlationId, StringComparison.OrdinalIgnoreCase)).ToList();

        return Ok(sessions.Select(s => new
        {
            testSessionId = s.TestSessionId,
            runtimeKind = s.RuntimeKind.ToString(),
            s.Status,
            s.CorrelationId,
            s.Mode,
            s.AgentId,
            s.ChannelId,
            s.ThreadId,
            s.CreatedAt,
            s.UpdatedAt
        }));
    }

    [HttpPost("sessions/{sessionId}/attachments")]
    public IActionResult RegisterAttachment(
        [FromRoute] string tenantId,
        [FromRoute] string sessionId,
        [FromBody] RegisterAttachmentRequest request)
    {
        if (!TryValidateTenant(tenantId, out var forbidden)) return forbidden!;
        var session = _store.Get(tenantId, sessionId);
        if (session is null) return NotFound();

        if (request.SizeBytes <= 0 || request.SizeBytes > 20 * 1024 * 1024)
            return BadRequest(new { errorCode = TestStudioErrorCatalog.AttachmentInvalidSize, message = "Attachment max size is 20MB." });

        if (!SupportedAttachmentContentTypes.Contains(request.ContentType))
            return BadRequest(new
            {
                errorCode = TestStudioErrorCatalog.AttachmentNotSupported,
                message = $"Unsupported attachment content type: {request.ContentType}"
            });

        var artifact = new TestStudioArtifact
        {
            AttachmentRef = $"att_{Guid.NewGuid():N}",
            Name = request.Name,
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes
        };
        _store.AddArtifact(tenantId, sessionId, artifact);
        _store.AppendEvent(tenantId, sessionId, new TestStudioEvent
        {
            Stage = "attachments",
            Direction = "inbound",
            PayloadType = "attachment",
            Status = "registered",
            CorrelationId = session.CorrelationId,
            Message = $"{artifact.Name} ({artifact.ContentType}) registered."
        });

        return Ok(artifact);
    }

    [HttpPost("sessions/{sessionId}/attachments/upload")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadAttachment(
        [FromRoute] string tenantId,
        [FromRoute] string sessionId,
        [FromForm] IFormFile file,
        CancellationToken ct)
    {
        if (!TryValidateTenant(tenantId, out var forbidden)) return forbidden!;
        var session = _store.Get(tenantId, sessionId);
        if (session is null) return NotFound();
        if (file is null || file.Length <= 0)
            return BadRequest(new { errorCode = TestStudioErrorCatalog.AttachmentInvalidSize, message = "File is required." });
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { errorCode = TestStudioErrorCatalog.AttachmentInvalidSize, message = "Attachment max size is 20MB." });
        if (!SupportedAttachmentContentTypes.Contains(file.ContentType))
            return BadRequest(new { errorCode = TestStudioErrorCatalog.AttachmentNotSupported, message = $"Unsupported attachment content type: {file.ContentType}" });

        var attachmentRef = $"att_{Guid.NewGuid():N}";
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var artifact = new TestStudioArtifact
        {
            AttachmentRef = attachmentRef,
            Name = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            Status = "uploaded"
        };
        _store.AddArtifact(tenantId, sessionId, artifact);
        _store.SaveArtifactContent(tenantId, sessionId, attachmentRef, bytes);
        _store.AppendEvent(tenantId, sessionId, new TestStudioEvent
        {
            Stage = "attachments",
            Direction = "inbound",
            PayloadType = "attachment",
            Status = "uploaded",
            CorrelationId = session.CorrelationId,
            Message = $"{artifact.Name} uploaded ({artifact.ContentType}, {artifact.SizeBytes} bytes)."
        });

        return Ok(artifact);
    }

    [HttpGet("sessions/{sessionId}/attachments/{attachmentRef}/download")]
    public IActionResult DownloadAttachment(
        [FromRoute] string tenantId,
        [FromRoute] string sessionId,
        [FromRoute] string attachmentRef)
    {
        if (!TryValidateTenant(tenantId, out var forbidden)) return forbidden!;
        var payload = _store.GetArtifactContent(tenantId, sessionId, attachmentRef);
        if (payload is null) return NotFound();
        return File(payload.Value.Content, payload.Value.ContentType, payload.Value.Name);
    }

    [HttpPost("sessions/{sessionId}/messages")]
    public async Task<IActionResult> SendMessage(
        [FromRoute] string tenantId,
        [FromRoute] string sessionId,
        [FromBody] TestStudioSendMessageRequest request,
        CancellationToken ct)
    {
        if (!TryValidateTenant(tenantId, out var forbidden)) return forbidden!;
        var session = _store.Get(tenantId, sessionId);
        if (session is null) return NotFound();
        if (!_store.TryConsumeMessageQuota(tenantId, sessionId, MaxMessagesPerMinutePerSession))
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                errorCode = TestStudioErrorCatalog.SessionRateLimited,
                message = $"Rate limit exceeded for session. Max {MaxMessagesPerMinutePerSession} messages/minute."
            });

        _store.AppendEvent(tenantId, sessionId, new TestStudioEvent
        {
            Stage = "input",
            Direction = "inbound",
            PayloadType = "text",
            Status = "accepted",
            CorrelationId = session.CorrelationId,
            Message = RedactBasicPii(request.Content)
        });

        if (session.RuntimeKind == AgentRuntimeKind.Text)
            return await HandleTextMessageAsync(tenantId, session, request, ct);

        if (session.RuntimeKind == AgentRuntimeKind.Voice)
        {
            _store.AppendEvent(tenantId, sessionId, new TestStudioEvent
            {
                Stage = "voice",
                Direction = "system",
                PayloadType = "voice_status",
                Status = "pending_external_call",
                CorrelationId = session.CorrelationId,
                Message = "Voice test session is ready. Continue via Twilio voice integration."
            });
            return Ok(new { status = "pending_external_call", message = "Voice runtime uses telephony integration in this phase." });
        }

        _store.AppendEvent(tenantId, sessionId, new TestStudioEvent
        {
            Stage = "multimodal",
            Direction = "system",
            PayloadType = "multimodal_status",
            Status = "contract_ready",
            CorrelationId = session.CorrelationId,
            Message = "Multimodal session contract is active. Full realtime transport is pending."
        });
        return Ok(new { status = "contract_ready", message = "Multimodal realtime transport is not enabled in this MVP." });
    }

    [HttpGet("sessions/{sessionId}/timeline")]
    public IActionResult GetTimeline([FromRoute] string tenantId, [FromRoute] string sessionId)
    {
        if (!TryValidateTenant(tenantId, out var forbidden)) return forbidden!;
        var session = _store.Get(tenantId, sessionId);
        if (session is null) return NotFound();

        return Ok(new
        {
            testSessionId = session.TestSessionId,
            runtimeKind = session.RuntimeKind.ToString(),
            status = session.Status,
            correlationId = session.CorrelationId,
            timelineEvents = _store.GetTimeline(tenantId, sessionId),
            artifacts = _store.GetArtifacts(tenantId, sessionId)
        });
    }

    [HttpPatch("sessions/{sessionId}/correlation")]
    public IActionResult UpdateCorrelation(
        [FromRoute] string tenantId,
        [FromRoute] string sessionId,
        [FromBody] UpdateCorrelationRequest request)
    {
        if (!TryValidateTenant(tenantId, out var forbidden)) return forbidden!;
        if (string.IsNullOrWhiteSpace(request.CorrelationId))
            return BadRequest(new { errorCode = TestStudioErrorCatalog.CorrelationRequired, message = "correlationId is required." });

        var session = _store.Get(tenantId, sessionId);
        if (session is null) return NotFound();

        var updated = _store.UpdateCorrelationId(tenantId, sessionId, request.CorrelationId.Trim());
        if (!updated) return NotFound();

        _store.AppendEvent(tenantId, sessionId, new TestStudioEvent
        {
            Stage = "session",
            Direction = "system",
            PayloadType = "correlation",
            Status = "updated",
            CorrelationId = request.CorrelationId.Trim(),
            Message = "Session correlationId updated."
        });

        return Ok(new { testSessionId = sessionId, correlationId = request.CorrelationId.Trim() });
    }

    [HttpPost("sessions/{sessionId}/close")]
    public IActionResult Close([FromRoute] string tenantId, [FromRoute] string sessionId)
    {
        if (!TryValidateTenant(tenantId, out var forbidden)) return forbidden!;
        if (!_store.Close(tenantId, sessionId)) return NotFound();
        return Ok(new { status = "completed" });
    }

    [HttpGet("sessions/{sessionId}/transcript")]
    public IActionResult GetTranscript([FromRoute] string tenantId, [FromRoute] string sessionId)
    {
        if (!TryValidateTenant(tenantId, out var forbidden)) return forbidden!;
        var session = _store.Get(tenantId, sessionId);
        if (session is null) return NotFound();

        var timeline = _store.GetTimeline(tenantId, sessionId);
        var entries = timeline
            .Where(e =>
                string.Equals(e.PayloadType, "text", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.PayloadType, "transcript", StringComparison.OrdinalIgnoreCase))
            .Select(e => new
            {
                e.Timestamp,
                speaker = string.Equals(e.Direction, "inbound", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant",
                text = e.Message ?? string.Empty,
                e.Stage,
                e.Status
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.text))
            .ToList();

        return Ok(new
        {
            testSessionId = session.TestSessionId,
            runtimeKind = session.RuntimeKind.ToString(),
            correlationId = session.CorrelationId,
            entries
        });
    }

    [HttpGet("metrics")]
    public IActionResult GetMetrics([FromRoute] string tenantId, [FromRoute] string runtimeKind)
    {
        if (!TryValidateTenant(tenantId, out var forbidden)) return forbidden!;
        if (!RuntimeCompatibilityPolicy.TryParseRuntimeKind(runtimeKind, out var kind, out _))
            return BadRequest(new { errorCode = TestStudioErrorCatalog.RuntimeIncompatible, message = "Invalid runtime kind." });

        var sessions = _store.ListByRuntime(tenantId, kind);
        var completed = sessions.Count(s => string.Equals(s.Status, "completed", StringComparison.OrdinalIgnoreCase));
        var active = sessions.Count(s => string.Equals(s.Status, "active", StringComparison.OrdinalIgnoreCase));

        var latenciesMs = new List<double>();
        var errorEvents = 0;
        foreach (var session in sessions)
        {
            var timeline = _store.GetTimeline(tenantId, session.TestSessionId);
            var input = timeline.FirstOrDefault(e => string.Equals(e.Stage, "input", StringComparison.OrdinalIgnoreCase));
            var response = timeline.LastOrDefault(e => string.Equals(e.Stage, "agent_response", StringComparison.OrdinalIgnoreCase));
            if (input is not null && response is not null && response.Timestamp >= input.Timestamp)
                latenciesMs.Add((response.Timestamp - input.Timestamp).TotalMilliseconds);

            errorEvents += timeline.Count(e => !string.IsNullOrWhiteSpace(e.ErrorCode) || string.Equals(e.Status, "failed", StringComparison.OrdinalIgnoreCase));
        }

        var successRate = sessions.Count == 0 ? 0 : Math.Round((double)completed * 100 / sessions.Count, 2);
        var avgLatency = latenciesMs.Count == 0 ? 0 : Math.Round(latenciesMs.Average(), 2);

        return Ok(new
        {
            runtimeKind = kind.ToString(),
            totalSessions = sessions.Count,
            activeSessions = active,
            completedSessions = completed,
            successRatePercent = successRate,
            avgE2eLatencyMs = avgLatency,
            totalErrorEvents = errorEvents
        });
    }

    private async Task<IActionResult> HandleTextMessageAsync(string tenantId, TestStudioSession session, TestStudioSendMessageRequest request, CancellationToken ct)
    {
        if (string.Equals(session.Mode, "channel", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(session.ChannelId))
                return BadRequest(new { errorCode = TestStudioErrorCatalog.ChannelRequired, message = "channelId is required in channel mode." });

            var channel = await _channelRepo.GetByIdAsync(session.ChannelId, tenantId, ct);
            if (channel is null) return NotFound(new { errorCode = TestStudioErrorCatalog.ChannelNotFound, message = "Channel not found." });

            var handler = _channelGateway.GetHandler(channel.Type);
            if (handler is null)
                return BadRequest(new { errorCode = TestStudioErrorCatalog.ChannelHandlerMissing, message = $"No handler for channel type {channel.Type}." });

            var from = ResolveUserId();
            var channelCtx = Domain.Common.ChannelContext.Create(channel.Type, channel.Id, session.CorrelationId, from, from);
            var channelSession = await handler.GetOrCreateSessionAsync(channelCtx, channel, ct);
            var incoming = ChannelMessage.CreateIncoming(tenantId, channel.Id, channelSession.Id, from, request.Content);
            incoming.Metadata["correlation_id"] = session.CorrelationId;
            var outgoing = await _channelGateway.ProcessMessageAsync(incoming, ct);
            _store.AppendEvent(tenantId, session.TestSessionId, new TestStudioEvent
            {
                Stage = "agent_response",
                Direction = "outbound",
                PayloadType = "text",
                Status = outgoing.Status.ToString(),
                CorrelationId = session.CorrelationId,
                Message = RedactBasicPii(outgoing.Content)
            });
            return Ok(new { status = outgoing.Status.ToString(), response = outgoing.Content, sessionId = channelSession.Id, executionId = outgoing.AgentExecutionId });
        }

        if (string.IsNullOrWhiteSpace(session.AgentId))
            return BadRequest(new { errorCode = TestStudioErrorCatalog.AgentRequired, message = "agentId is required." });

        var agent = await _agentRepo.GetByIdAsync(session.AgentId, tenantId, ct);
        if (agent is null) return NotFound(new { errorCode = TestStudioErrorCatalog.AgentNotFound, message = "Agent not found." });
        if (agent.Session.RuntimeKind != AgentRuntimeKind.Text)
            return BadRequest(new { errorCode = TestStudioErrorCatalog.RuntimeIncompatible, message = "Agent runtime is not Text." });

        var attachmentRefs = request.AttachmentRefs?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray() ?? [];
        var metadata = new Dictionary<string, string>
        {
            ["testStudioSessionId"] = session.TestSessionId,
            ["testStudioMode"] = session.Mode,
            ["attachmentRefs"] = string.Join(",", attachmentRefs)
        };
        if (attachmentRefs.Length > 0)
        {
            _store.AppendEvent(tenantId, session.TestSessionId, new TestStudioEvent
            {
                Stage = "attachments",
                Direction = "system",
                PayloadType = "attachment",
                Status = "accepted_metadata",
                CorrelationId = session.CorrelationId,
                Message = "Attachment refs forwarded. OCR/extraction can be limited depending on runtime path."
            });
        }

        var exec = await _executor.ExecuteAsync(new AgentExecutionRequest
        {
            TenantId = tenantId,
            AgentKey = session.AgentId,
            UserId = ResolveUserId(),
            UserMessage = request.Content,
            CorrelationId = session.CorrelationId,
            ThreadId = session.ThreadId,
            Metadata = metadata
        }, ct);

        _store.AppendEvent(tenantId, session.TestSessionId, new TestStudioEvent
        {
            Stage = "agent_response",
            Direction = "outbound",
            PayloadType = "text",
            Status = exec.Status.ToString(),
            CorrelationId = session.CorrelationId,
            ErrorCode = exec.ErrorCode,
            Message = RedactBasicPii(exec.FinalResponse ?? exec.ErrorMessage)
        });

        return Ok(new
        {
            status = exec.Status.ToString(),
            executionId = exec.ExecutionId,
            response = exec.FinalResponse,
            errorCode = exec.ErrorCode,
            errorMessage = exec.ErrorMessage
        });
    }

    private object ToResponse(TestStudioSession session, string tenantId) => new
    {
        testSessionId = session.TestSessionId,
        runtimeKind = session.RuntimeKind.ToString(),
        channelType = session.ChannelType,
        status = session.Status,
        correlationId = session.CorrelationId,
        mode = session.Mode,
        agentId = session.AgentId,
        channelId = session.ChannelId,
        threadId = session.ThreadId,
        timelineEvents = _store.GetTimeline(tenantId, session.TestSessionId),
        artifacts = _store.GetArtifacts(tenantId, session.TestSessionId)
    };

    private bool TryValidateTenant(string tenantId, out IActionResult? forbidden)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin)
        {
            forbidden = Forbid();
            return false;
        }
        forbidden = null;
        return true;
    }

    private string ResolveUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? User.FindFirstValue("sub")
           ?? _tenantContext.Current?.UserId
           ?? "anonymous-user";

    private static string? RedactBasicPii(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        var output = System.Text.RegularExpressions.Regex.Replace(
            input,
            @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}",
            "[redacted-email]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        output = System.Text.RegularExpressions.Regex.Replace(output, @"\+?\d[\d\s\-()]{7,}\d", "[redacted-phone]");
        return output;
    }
}

public sealed record CreateTestStudioSessionRequest
{
    public string? Mode { get; init; } = "direct";
    public string? AgentId { get; init; }
    public string? ChannelId { get; init; }
    public string? ChannelType { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record RegisterAttachmentRequest
{
    public required string Name { get; init; }
    public required string ContentType { get; init; }
    public long SizeBytes { get; init; }
}

public sealed record TestStudioSendMessageRequest
{
    public required string Content { get; init; }
    public IReadOnlyList<string>? AttachmentRefs { get; init; }
}

public sealed record UpdateCorrelationRequest
{
    public required string CorrelationId { get; init; }
}
