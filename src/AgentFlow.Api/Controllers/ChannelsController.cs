using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/channels")]
[Authorize]
public sealed class ChannelsController : ControllerBase
{
    private readonly IChannelDefinitionRepository _channelRepo;
    private readonly IChannelGateway _gateway;
    private readonly ITenantContextAccessor _tenantContext;

    public ChannelsController(
        IChannelDefinitionRepository channelRepo,
        IChannelGateway gateway,
        ITenantContextAccessor tenantContext)
    {
        _channelRepo = channelRepo;
        _gateway = gateway;
        _tenantContext = tenantContext;
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

    [HttpPost("{channelId}/activate")]
    public async Task<IActionResult> Activate(string tenantId, string channelId, CancellationToken ct)
    {
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
}

public sealed record CreateChannelRequest
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public Dictionary<string, string>? Config { get; init; }
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
