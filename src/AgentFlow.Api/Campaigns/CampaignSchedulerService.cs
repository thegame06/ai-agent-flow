using AgentFlow.Abstractions.Connect;
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
                var dueCampaigns = await store.GetDueCampaignsAsync(DateTimeOffset.UtcNow, stoppingToken);
                foreach (var campaign in dueCampaigns)
                {
                    try
                    {
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
