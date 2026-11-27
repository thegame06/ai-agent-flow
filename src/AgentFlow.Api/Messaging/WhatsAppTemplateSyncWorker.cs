using AgentFlow.Abstractions;

namespace AgentFlow.Api.Messaging;

/// <summary>
/// Re-syncs the templates of every tenant that has them (default every 6 hours). Status changes
/// normally arrive by webhook; this catches the ones a missed webhook would leave stale.
/// </summary>
public sealed class WhatsAppTemplateSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WhatsAppTemplateSyncWorker> _logger;
    private readonly TimeSpan _interval;

    public WhatsAppTemplateSyncWorker(IServiceScopeFactory scopes, ILogger<WhatsAppTemplateSyncWorker> logger, IConfiguration configuration)
    {
        _scopes = scopes;
        _logger = logger;
        _interval = TimeSpan.FromHours(Math.Max(1, configuration.GetValue("WhatsApp:TemplateSyncHours", 6)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Not right at startup: let the API come up first.
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IWhatsAppTemplateStore>();
                var templates = scope.ServiceProvider.GetRequiredService<IWhatsAppTemplateService>();
                var locks = scope.ServiceProvider.GetService<IDistributedLockService>();
                foreach (var tenantId in await store.GetTenantIdsAsync(stoppingToken))
                {
                    await using var held = locks is null ? null : await locks.TryAcquireAsync($"whatsapp-template-sync:{tenantId}", TimeSpan.FromMinutes(10), stoppingToken);
                    if (locks is not null && held is null) continue;
                    var result = await templates.SyncAsync(tenantId, null, "template-sync", stoppingToken);
                    if (result.Errors.Count > 0)
                        _logger.LogWarning("Template sync for {TenantId} had errors: {Errors}", tenantId, string.Join("; ", result.Errors));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "WhatsApp template sync iteration failed.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
