using AgentFlow.Abstractions;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Api.Campaigns;

public sealed class CampaignSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CampaignSchedulerService> _logger;

    public CampaignSchedulerService(IServiceScopeFactory scopeFactory, ILogger<CampaignSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<ICampaignStore>();
                var executor = scope.ServiceProvider.GetRequiredService<ICampaignExecutionService>();
                var entitlements = scope.ServiceProvider.GetRequiredService<IAppEntitlementService>();
                var locks = scope.ServiceProvider.GetService<IDistributedLockService>();
                var dueCampaigns = await store.GetDueCampaignsAsync(DateTimeOffset.UtcNow, stoppingToken);
                foreach (var due in dueCampaigns)
                {
                    // With several API instances every one sees the same due campaigns: only the
                    // instance holding the lock runs it, and it re-reads the campaign so a run that
                    // another instance just finished is not repeated.
                    await using var held = locks is null ? null : await locks.TryAcquireAsync($"campaign-run:{due.TenantId}:{due.Id}", TimeSpan.FromMinutes(30), stoppingToken);
                    if (locks is not null && held is null) continue;
                    var campaign = await store.GetCampaignAsync(due.TenantId, due.Id, stoppingToken);
                    if (campaign is null || campaign.NextRunAt is null || campaign.NextRunAt > DateTimeOffset.UtcNow) continue;
                    try
                    {
                        // A tenant without the Campaigns app must not get scheduled sends.
                        // The campaign stays due and runs on the next tick after the app is enabled.
                        if (!await entitlements.IsEnabledAsync(campaign.TenantId, AppModules.Campaigns, stoppingToken))
                        {
                            _logger.LogDebug("Skipping scheduled campaign: Campaigns app disabled. Tenant={TenantId} CampaignId={CampaignId}", campaign.TenantId, campaign.Id);
                            continue;
                        }

                        // Outside the contact window (e.g. 8:00-20:00 in the campaign's timezone): move the
                        // run to when the window opens instead of messaging customers at night.
                        var window = CampaignContactPolicy.From(campaign);
                        var now = DateTimeOffset.UtcNow;
                        if (!window.IsOpen(now))
                        {
                            await store.UpsertCampaignAsync(campaign with { NextRunAt = window.NextOpen(now) }, stoppingToken);
                            continue;
                        }

                        await executor.RunNowAsync(campaign.TenantId, campaign.Id, "campaign-scheduler", CampaignRunTrigger.Schedule, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Scheduled campaign run failed. Tenant={TenantId} CampaignId={CampaignId}", campaign.TenantId, campaign.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Campaign scheduler iteration failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
