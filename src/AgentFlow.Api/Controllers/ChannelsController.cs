using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Intents.Catalog;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/channels")]
[Authorize]
public sealed class ChannelsController : ControllerBase
{
    private readonly IChannelDefinitionRepository _channelRepo;
    private readonly IChannelGateway _gateway;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IIntentCatalogService _intentCatalog;
    private readonly IIntentRoutingStore _intentRoutingStore;

    public ChannelsController(
        IChannelDefinitionRepository channelRepo,
        IChannelGateway gateway,
        ITenantContextAccessor tenantContext,
        IIntentCatalogService intentCatalog,
        IIntentRoutingStore intentRoutingStore)
    {
        _channelRepo = channelRepo;
        _gateway = gateway;
        _tenantContext = tenantContext;
        _intentCatalog = intentCatalog;
        _intentRoutingStore = intentRoutingStore;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(string tenantId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var channels = await _channelRepo.GetAllAsync(tenantId, ct);
        return Ok(channels.Select(c => new ChannelDto
        {
            Id = c.Id,
            Name = c.Name,
            Type = c.Type.ToString(),
            Status = c.Status.ToString(),
            Config = c.Config,
            CreatedAt = c.CreatedAt,
            LastActivityAt = c.LastActivityAt
        }));
    }

    [HttpGet("{channelId}")]
    public async Task<IActionResult> GetById(string tenantId, string channelId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var channel = await _channelRepo.GetByIdAsync(channelId, tenantId, ct);
        if (channel == null) return NotFound();

        return Ok(new ChannelDto
        {
            Id = channel.Id,
            Name = channel.Name,
            Type = channel.Type.ToString(),
            Status = channel.Status.ToString(),
            Config = channel.Config,
            CreatedAt = channel.CreatedAt,
            LastActivityAt = channel.LastActivityAt
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(string tenantId, [FromBody] CreateChannelRequest request, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        if (!Enum.TryParse<ChannelType>(request.Type, true, out var channelType))
            return BadRequest(new { message = "Invalid channel type" });

        var channel = ChannelDefinition.Create(tenantId, request.Name, channelType, request.Config);

        if (request.SessionWindowHours.HasValue)
            channel.SetSessionWindowHours(request.SessionWindowHours.Value);

        if (!string.IsNullOrWhiteSpace(request.RouterAgentId))
            channel.SetRouterAgentId(request.RouterAgentId);

        if (!string.IsNullOrWhiteSpace(request.ReopenTemplateName))
            channel.SetReopenTemplateName(request.ReopenTemplateName);
        
        var result = await _channelRepo.InsertAsync(channel, ct);
        if (!result.IsSuccess) return BadRequest(result.Error);

        return CreatedAtAction(nameof(GetById), new { tenantId, channelId = channel.Id }, new
        {
            channel.Id,
            channel.Name,
            Type = channel.Type.ToString(),
            Status = channel.Status.ToString()
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // POST /{channelId}/messages — omnichannel message entry point
    //
    // This is the UNIFIED entry point for any system or integration that wants
    // to send a message through AgentFlow without owning a native channel SDK.
    //
    // Two delivery modes, selected by the caller:
    //
    //   SYNC  (default, no CallbackUrl):
    //     The HTTP request blocks until the agent finishes.
    //     Response is returned inline in the HTTP body.
    //     Best for: WebChat widget, internal tooling, low-latency integrations.
    //
    //   ASYNC (CallbackUrl provided):
    //     Returns HTTP 202 immediately with a correlationId.
    //     When the agent finishes, AgentFlow POSTs the result to CallbackUrl.
    //     Best for: 3rd-party integrations, serverless backends, long workflows.
    //
    // Transport delivery (the actual send to end-user) is always handled by
    // handler.SendReplyAsync — which is channel-specific:
    //   WhatsApp  → WhatsApp Business API / QR Bridge
    //   WebChat   → inline response or SSE buffer
    //   Api/sync  → returned in this HTTP response
    //   Api/async → POSTed to CallbackUrl
    //   Slack     → Slack Web API        (future)
    //   Telegram  → Telegram Bot API     (future)
    // ──────────────────────────────────────────────────────────────────────────
    [HttpPost("{channelId}/messages")]
    public async Task<IActionResult> SendMessage(
        string tenantId,
        string channelId,
        [FromBody] ChannelMessageRequest request,
        CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { message = "Content is required." });

        var channel = await _channelRepo.GetByIdAsync(channelId, tenantId, ct);
        if (channel is null) return NotFound(new { message = "Channel not found." });

        if (channel.Status != ChannelStatus.Active)
            return BadRequest(new { message = $"Channel is not active (status: {channel.Status})." });

        // If async mode, override the channel's webhook URL for this specific call.
        // This lets the caller provide a one-time callback per message.
        if (!string.IsNullOrWhiteSpace(request.CallbackUrl))
        {
            var patched = new Dictionary<string, string>(channel.Config, StringComparer.OrdinalIgnoreCase)
            {
                ["WebhookCallbackUrl"] = request.CallbackUrl
            };
            channel.UpdateConfig(patched);
        }

        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var from = request.From ?? context.UserId;

        // Build a synthetic session for this message — reuse existing or create new
        var handler = _gateway.GetHandler(channel.Type);
        if (handler is null)
            return BadRequest(new { message = $"No handler registered for channel type {channel.Type}." });

        var channelCtx = Domain.Common.ChannelContext.Create(
            channel.Type, channel.Id, correlationId, from, request.DisplayName);
        var session = await handler.GetOrCreateSessionAsync(channelCtx, channel, ct);

        var incoming = AgentFlow.Domain.Aggregates.ChannelMessage.CreateIncoming(
            tenantId:   tenantId,
            channelId:  channelId,
            sessionId:  session.Id,
            from:       from,
            content:    request.Content);

        incoming.Metadata["correlation_id"] = correlationId;
        if (request.Metadata is not null)
        {
            foreach (var kv in request.Metadata)
                incoming.Metadata.TryAdd(kv.Key, kv.Value);
        }

        // ASYNC mode: accept immediately, process in background
        if (!string.IsNullOrWhiteSpace(request.CallbackUrl))
        {
            _ = Task.Run(async () =>
            {
                try { await _gateway.ProcessMessageAsync(incoming, CancellationToken.None); }
                catch (Exception ex)
                {
                    // Errors in async mode are delivered to CallbackUrl by the channel handler.
                    // If even that fails, they are only observable in the logs + OpenTelemetry.
                    var logger = HttpContext.RequestServices
                        .GetRequiredService<Microsoft.Extensions.Logging.ILogger<ChannelsController>>();
                    logger.LogError(ex,
                        "Async message processing failed for channel {ChannelId} correlation {CorrelationId}",
                        channelId, correlationId);
                }
            }, CancellationToken.None);

            return Accepted(new
            {
                correlationId,
                sessionId  = session.Id,
                channelId,
                mode       = "async",
                callbackUrl = request.CallbackUrl,
                message    = "Message accepted. Response will be delivered to callbackUrl."
            });
        }

        // SYNC mode: block until agent responds, return inline
        var outgoing = await _gateway.ProcessMessageAsync(incoming, ct);
        if (outgoing.Direction == MessageDirection.Incoming && outgoing.Status == MessageStatus.Failed)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                correlationId,
                sessionId = session.Id,
                channelId,
                mode = "sync",
                delivered = false,
                incomingMessageId = outgoing.Id,
                executionId = outgoing.AgentExecutionId,
                error = outgoing.ErrorMessage,
                failureLevel = outgoing.Metadata.GetValueOrDefault("agentflow.failure_level"),
                message = "AgentFlow did not send a customer reply because processing failed."
            });
        }

        return Ok(new
        {
            correlationId,
            sessionId     = session.Id,
            channelId,
            mode          = "sync",
            delivered     = true,
            response      = outgoing.Content,
            messageId     = outgoing.Id,
            executionId   = outgoing.AgentExecutionId
        });
    }

    [HttpPost("{channelId}/activate")]
    public async Task<IActionResult> Activate(string tenantId, string channelId, CancellationToken ct)    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var channel = await _channelRepo.GetByIdAsync(channelId, tenantId, ct);
        if (channel == null) return NotFound();


        var handler = _gateway.GetHandler(channel.Type);
        if (handler == null) return BadRequest(new { message = $"No handler for channel type {channel.Type}" });

        var status = await handler.InitializeAsync(channel, ct);
        await _channelRepo.UpdateAsync(channel, ct);

        return Ok(new { channel.Id, Status = status.ToString() });
    }

    [HttpPost("{channelId}/assign-agent")]
    public async Task<IActionResult> AssignAgent(string tenantId, string channelId, [FromBody] AgentFlow.Api.Contracts.AssignChannelAgentRequest request, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        if (string.IsNullOrWhiteSpace(request.AgentId))
            return BadRequest(new { message = "AgentId is required" });

        var channel = await _channelRepo.GetByIdAsync(channelId, tenantId, ct);
        if (channel == null) return NotFound();

        // TODO: Domain aggregate currently has no AssignAgent mutation method.
        // Keep endpoint contract for frontend integration, but return explicit pending status.
        return StatusCode(StatusCodes.Status501NotImplemented, new
        {
            message = "Assign-agent mutation is pending in domain model. Use channel creation with AgentId for now.",
            channelId = channel.Id,
            request.AgentId
        });
    }

    [HttpGet("{channelId}/routing")]
    public async Task<IActionResult> GetRouting(string tenantId, string channelId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var channel = await _channelRepo.GetByIdAsync(channelId, tenantId, ct);
        if (channel == null) return NotFound();

        var routingAgents = channel.Config.GetValueOrDefault("RoutingAgents") ?? string.Empty;
        var defaultAgentId = channel.Config.GetValueOrDefault("DefaultAgentId") ?? string.Empty;

        return Ok(new ChannelRoutingDto
        {
            ChannelId = channel.Id,
            DefaultAgentId = defaultAgentId,
            RoutingAgents = routingAgents
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList()
        });
    }

    [HttpPost("{channelId}/routing")]
    public async Task<IActionResult> UpdateRouting(string tenantId, string channelId, [FromBody] UpdateChannelRoutingRequest request, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var channel = await _channelRepo.GetByIdAsync(channelId, tenantId, ct);
        if (channel == null) return NotFound();

        var updated = new Dictionary<string, string>(channel.Config, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.DefaultAgentId))
            updated["DefaultAgentId"] = request.DefaultAgentId.Trim();

        var routing = request.RoutingAgents?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        updated["RoutingAgents"] = string.Join(",", routing);
        if (request.RoutingCapacities is not null)
        {
            var capacityCsv = string.Join(",",
                request.RoutingCapacities
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value > 0)
                    .Select(kv => $"{kv.Key.Trim()}:{kv.Value}"));
            updated["RoutingCapacities"] = capacityCsv;
        }
        channel.UpdateConfig(updated);

        var result = await _channelRepo.UpdateAsync(channel, ct);
        if (!result.IsSuccess) return BadRequest(result.Error);

        return Ok(new ChannelRoutingDto
        {
            ChannelId = channel.Id,
            DefaultAgentId = updated.GetValueOrDefault("DefaultAgentId") ?? string.Empty,
            RoutingAgents = routing,
            RoutingCapacities = ParseRoutingCapacities(updated.GetValueOrDefault("RoutingCapacities"))
        });
    }

    [HttpGet("{channelId}/intents/catalog")]
    public async Task<IActionResult> GetChannelIntentCatalog(string tenantId, string channelId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var channel = await _channelRepo.GetByIdAsync(channelId, tenantId, ct);
        if (channel == null) return NotFound();

        var channelKey = channel.Type.ToString().ToLowerInvariant();
        var baseIntents = await _intentCatalog.GetBaseIntentsAsync(ct);
        var rules = await _intentRoutingStore.GetRulesByChannelAsync(tenantId, channelKey, ct);

        var items = baseIntents
            .OrderBy(i => i.Priority)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i =>
            {
                var matched = rules.FirstOrDefault(r => string.Equals(r.IntentKey, i.Key, StringComparison.OrdinalIgnoreCase));
                return new ChannelIntentCatalogItemDto
                {
                    Key = i.Key,
                    Name = i.Name,
                    Description = i.Description,
                    Category = i.Category,
                    Priority = i.Priority,
                    Examples = i.Examples,
                    Selected = matched is not null
                };
            })
            .ToList();

        return Ok(new ChannelIntentCatalogDto
        {
            ChannelId = channelId,
            ChannelType = channel.Type.ToString(),
            RouterAgentId = channel.RouterAgentId,
            DefaultAgentId = channel.Config.GetValueOrDefault("DefaultAgentId") ?? string.Empty,
            Items = items
        });
    }

    [HttpPost("{channelId}/intents/apply")]
    public async Task<IActionResult> ApplyChannelIntentCatalog(
        string tenantId,
        string channelId,
        [FromBody] ApplyChannelIntentCatalogRequest request,
        CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var channel = await _channelRepo.GetByIdAsync(channelId, tenantId, ct);
        if (channel == null) return NotFound();

        var sourceAgentId = !string.IsNullOrWhiteSpace(channel.RouterAgentId)
            ? channel.RouterAgentId
            : channel.Config.GetValueOrDefault("DefaultAgentId") ?? "router";
        var targetAgentId = channel.Config.GetValueOrDefault("DefaultAgentId") ?? sourceAgentId;
        if (string.Equals(sourceAgentId, targetAgentId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "No se puede cargar intenciones: el canal tiene el mismo agente como origen y destino. Configure un DefaultAgentId distinto al RouterAgentId."
            });
        }
        var channelKey = channel.Type.ToString().ToLowerInvariant();

        var baseIntents = await _intentCatalog.GetBaseIntentsAsync(ct);
        var catalogByKey = baseIntents.ToDictionary(i => i.Key, StringComparer.OrdinalIgnoreCase);
        var selectedKeys = (request.IntentKeys ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingRules = await _intentRoutingStore.GetRulesByChannelAsync(tenantId, channelKey, ct);
        var managedRules = existingRules
            .Where(r => IsManagedByChannelIntentLoader(r, channelId))
            .ToList();

        var createdOrUpdated = 0;
        foreach (var intentKey in selectedKeys)
        {
            if (!catalogByKey.TryGetValue(intentKey, out var definition))
                continue;

            var existing = managedRules.FirstOrDefault(r => string.Equals(r.IntentKey, intentKey, StringComparison.OrdinalIgnoreCase))
                ?? existingRules.FirstOrDefault(r =>
                    string.Equals(r.IntentKey, intentKey, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.SourceAgentId, sourceAgentId, StringComparison.OrdinalIgnoreCase));

            var saved = await _intentRoutingStore.UpsertRuleAsync(new IntentRoutingRule
            {
                Id = existing?.Id ?? $"channel-{channelId}-intent-{definition.Key}",
                TenantId = tenantId,
                IntentKey = definition.Key,
                IntentDescription = definition.Description,
                ExamplePhrases = definition.Examples,
                SourceAgentId = sourceAgentId,
                TargetAgentId = targetAgentId,
                WorkflowDefinitionId = existing?.WorkflowDefinitionId,
                WorkflowName = existing?.WorkflowName,
                Priority = definition.Priority,
                Enabled = true,
                Channel = channelKey,
                ConditionsJson = JsonSerializer.Serialize(new { managedBy = "channel-intent-loader", channelId }),
                HandoffPolicyJson = existing?.HandoffPolicyJson,
                Version = existing?.Version ?? 1,
                CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct);

            if (!string.IsNullOrWhiteSpace(saved.Id))
                createdOrUpdated++;
        }

        var removed = 0;
        foreach (var stale in managedRules.Where(r => !selectedKeys.Contains(r.IntentKey)))
        {
            var ok = await _intentRoutingStore.DeleteRuleAsync(tenantId, stale.Id, ct);
            if (ok)
                removed++;
        }

        return Ok(new
        {
            channelId,
            applied = selectedKeys.Count,
            createdOrUpdated,
            removed,
            sourceAgentId,
            targetAgentId
        });
    }

    [HttpGet("{channelId}/routing/preview")]
    public async Task<IActionResult> PreviewRouting(string tenantId, string channelId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var channel = await _channelRepo.GetByIdAsync(channelId, tenantId, ct);
        if (channel == null) return NotFound();

        var routingAgents = (channel.Config.GetValueOrDefault("RoutingAgents") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var capacities = ParseRoutingCapacities(channel.Config.GetValueOrDefault("RoutingCapacities"));
        var active = await HttpContext.RequestServices.GetRequiredService<IChannelSessionRepository>()
            .GetActiveByChannelAsync(channel.Id, channel.TenantId, ct);
        var loadByAgent = active
            .Where(s => !string.IsNullOrWhiteSpace(s.AgentId))
            .GroupBy(s => s.AgentId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var candidatePool = routingAgents.Count > 0 ? routingAgents : new List<string> { channel.Config.GetValueOrDefault("DefaultAgentId") ?? string.Empty };
        candidatePool = candidatePool.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var withCapacity = candidatePool
            .Where(agentId => !capacities.TryGetValue(agentId, out var max) || (loadByAgent.TryGetValue(agentId, out var current) ? current : 0) < max)
            .ToList();
        var pool = withCapacity.Count > 0 ? withCapacity : candidatePool;
        var suggested = pool
            .OrderBy(a => loadByAgent.TryGetValue(a, out var count) ? count : 0)
            .ThenBy(a => a, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var loads = candidatePool.ToDictionary(
            a => a,
            a => loadByAgent.TryGetValue(a, out var count) ? count : 0,
            StringComparer.OrdinalIgnoreCase);

        return Ok(new ChannelRoutingPreviewDto
        {
            ChannelId = channel.Id,
            SuggestedAgentId = suggested,
            ActiveLoadByAgent = loads,
            RoutingCapacities = capacities
        });
    }

    [HttpPost("{channelId}/deactivate")]
    public async Task<IActionResult> Deactivate(string tenantId, string channelId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var channel = await _channelRepo.GetByIdAsync(channelId, tenantId, ct);
        if (channel == null) return NotFound();

        var handler = _gateway.GetHandler(channel.Type);
        if (handler != null)
            await handler.ShutdownAsync(channel, ct);

        channel.Deactivate();
        await _channelRepo.UpdateAsync(channel, ct);

        return Ok(new { channel.Id, Status = channel.Status.ToString() });
    }

    [HttpGet("{channelId}/qr")]
    public async Task<IActionResult> GetQr(string tenantId, string channelId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var channel = await _channelRepo.GetByIdAsync(channelId, tenantId, ct);
        if (channel == null) return NotFound();

        if (channel.Type != ChannelType.WhatsApp)
            return BadRequest(new { message = "QR endpoint only available for WhatsApp channels" });

        var handler = _gateway.GetHandler(channel.Type) as AgentFlow.Application.Channels.IChannelQrProvider;
        if (handler == null) return BadRequest(new { message = "WhatsApp handler does not support QR" });

        var qrCode = await handler.GetQrCodeAsync(ct);
        if (string.IsNullOrWhiteSpace(qrCode))
            return NotFound(new { message = "QR code not available yet" });

        return Ok(new { qrCode });
    }

    [HttpGet("{channelId}/status")]
    public async Task<IActionResult> GetStatus(string tenantId, string channelId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var channel = await _channelRepo.GetByIdAsync(channelId, tenantId, ct);
        if (channel == null) return NotFound();

        var handler = _gateway.GetHandler(channel.Type);
        if (handler == null) return BadRequest(new { message = $"No handler for channel type {channel.Type}" });

        var health = await handler.CheckHealthAsync(channel, ct);

        string? qrCode = null;
        if (channel.Type == ChannelType.WhatsApp && channel.Config.GetValueOrDefault("AuthMode") == "qr")
        {
            var qrHandler = handler as AgentFlow.Application.Channels.IChannelQrProvider;
            if (qrHandler != null)
                qrCode = await qrHandler.GetQrCodeAsync(ct);
        }

        return Ok(new
        {
            channel.Id,
            channel.Status,
            health.Healthy,
            health.Message,
            health.CheckedAt,
            qrAvailable = !health.Healthy && !string.IsNullOrWhiteSpace(qrCode)
        });
    }

    [HttpPost("{channelId}/health")]
    public async Task<IActionResult> CheckHealth(string tenantId, string channelId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var channel = await _channelRepo.GetByIdAsync(channelId, tenantId, ct);
        if (channel == null) return NotFound();

        var handler = _gateway.GetHandler(channel.Type);
        if (handler == null) return BadRequest(new { message = $"No handler for channel type {channel.Type}" });

        var health = await handler.CheckHealthAsync(channel, ct);
        return Ok(new
        {
            channel.Id,
            health.Healthy,
            health.Message,
            health.CheckedAt
        });
    }

    [HttpDelete("{channelId}")]
    public async Task<IActionResult> Delete(string tenantId, string channelId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (context.TenantId != tenantId && !context.IsPlatformAdmin) return Forbid();

        var channel = await _channelRepo.GetByIdAsync(channelId, tenantId, ct);
        if (channel == null) return NotFound();

        var handler = _gateway.GetHandler(channel.Type);
        if (handler != null)
            await handler.ShutdownAsync(channel, ct);

        await _channelRepo.DeleteAsync(channelId, tenantId, ct);
        return NoContent();
    }

    private static Dictionary<string, int> ParseRoutingCapacities(string? raw)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        var entries = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            var parts = entry.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
                continue;
            if (!int.TryParse(parts[1], out var cap) || cap <= 0)
                continue;
            result[parts[0]] = cap;
        }

        return result;
    }

    private static bool IsManagedByChannelIntentLoader(IntentRoutingRule rule, string channelId)
    {
        if (rule.Id.StartsWith($"channel-{channelId}-intent-", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(rule.ConditionsJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(rule.ConditionsJson);
            if (!doc.RootElement.TryGetProperty("managedBy", out var managedBy))
                return false;
            if (!string.Equals(managedBy.GetString(), "channel-intent-loader", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!doc.RootElement.TryGetProperty("channelId", out var ownerChannel))
                return false;
            return string.Equals(ownerChannel.GetString(), channelId, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

public sealed record CreateChannelRequest
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public Dictionary<string, string>? Config { get; init; }

    /// <summary>
    /// Duration of the open conversation window in hours (default: 24).
    /// After expiry the channel sends a template message to re-open the window.
    /// </summary>
    public int? SessionWindowHours { get; init; }

    /// <summary>ID of the Router agent to assign to this channel.</summary>
    public string? RouterAgentId { get; init; }

    /// <summary>WhatsApp-approved template name to use when the session window closes.</summary>
    public string? ReopenTemplateName { get; init; }
}

public sealed record ChannelDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Status { get; init; }
    public Dictionary<string, string> Config { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastActivityAt { get; init; }
}

public sealed record UpdateChannelRoutingRequest
{
    public string? DefaultAgentId { get; init; }
    public List<string>? RoutingAgents { get; init; }
    public Dictionary<string, int>? RoutingCapacities { get; init; }
}

public sealed record ChannelRoutingDto
{
    public required string ChannelId { get; init; }
    public required string DefaultAgentId { get; init; }
    public List<string> RoutingAgents { get; init; } = new();
    public Dictionary<string, int> RoutingCapacities { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record ChannelRoutingPreviewDto
{
    public required string ChannelId { get; init; }
    public string? SuggestedAgentId { get; init; }
    public Dictionary<string, int> ActiveLoadByAgent { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> RoutingCapacities { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record ChannelIntentCatalogItemDto
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required int Priority { get; init; }
    public IReadOnlyList<string> Examples { get; init; } = Array.Empty<string>();
    public required bool Selected { get; init; }
}

public sealed record ChannelIntentCatalogDto
{
    public required string ChannelId { get; init; }
    public required string ChannelType { get; init; }
    public string? RouterAgentId { get; init; }
    public string DefaultAgentId { get; init; } = string.Empty;
    public IReadOnlyList<ChannelIntentCatalogItemDto> Items { get; init; } = Array.Empty<ChannelIntentCatalogItemDto>();
}

public sealed record ApplyChannelIntentCatalogRequest
{
    public IReadOnlyList<string> IntentKeys { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Request body for POST /{channelId}/messages.
/// Supports both sync (inline response) and async (webhook callback) delivery modes.
/// </summary>
public sealed record ChannelMessageRequest
{
    /// <summary>Message content to send to the agent.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// Identifier of the sender — e.g. "user-123", "system", an email, a phone number.
    /// Defaults to the authenticated user's ID if not provided.
    /// </summary>
    public string? From { get; init; }

    /// <summary>Display name of the sender shown in conversation history.</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Optional correlation ID to trace this message across your system.
    /// If omitted, AgentFlow generates one and returns it in the response.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// ASYNC mode: provide a URL and the request returns HTTP 202 immediately.
    /// AgentFlow will POST the agent response to this URL when ready.
    /// The payload matches the sync response shape plus a top-level "event": "message.completed".
    ///
    /// SYNC mode: omit this field. The request blocks and returns the response inline.
    /// </summary>
    public string? CallbackUrl { get; init; }

    /// <summary>Extra key-value pairs forwarded to the agent's execution context.</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
