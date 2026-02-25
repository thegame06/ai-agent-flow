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
