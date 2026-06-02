using System.Text.Json;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Commerce;
using AgentFlow.Api.Connect;
using AgentFlow.Domain.Repositories;

namespace AgentFlow.Api.Campaigns;

public interface ICampaignAudienceService
{
    Task<CampaignAudiencePreviewContract> PreviewAsync(
        string tenantId,
        string filterJson,
        string? campaignId,
        CancellationToken ct = default);
}

public sealed class CampaignAudienceService : ICampaignAudienceService
{
    private readonly ICommerceStore _commerce;
    private readonly IConnectStore _connectStore;
    private readonly ICampaignStore _campaignStore;
    private readonly IChannelSessionRepository _sessionRepo;

    public CampaignAudienceService(
        ICommerceStore commerce,
        IConnectStore connectStore,
        ICampaignStore campaignStore,
        IChannelSessionRepository sessionRepo)
    {
        _commerce = commerce;
        _connectStore = connectStore;
        _campaignStore = campaignStore;
        _sessionRepo = sessionRepo;
    }

    public async Task<CampaignAudiencePreviewContract> PreviewAsync(
        string tenantId,
        string filterJson,
        string? campaignId,
        CancellationToken ct = default)
    {
        var filter = ParseFilter(filterJson);
        var parties = await _commerce.SearchPartiesAsync(tenantId, filter.Query, 0, 1000, ct);
        var inbox = await _connectStore.GetInboxAsync(tenantId, 5000, ct);
        var warnings = new List<string>();
        if (parties.Count >= 1000)
            warnings.Add("La vista previa usa los primeros 1000 clientes/leads. Ajusta filtros si esperas una audiencia mayor.");

        var contacts = new List<CampaignAudienceContactContract>();
        foreach (var party in parties)
        {
            var sales = await _commerce.SearchSalesAsync(tenantId, party.Id, null, 0, 100, ct);
            var invoices = await _commerce.SearchInvoicesAsync(tenantId, party.Id, null, 0, 100, ct);
            var matchedMessages = inbox
                .Where(x => string.Equals(x.Recipient, party.Identifier, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(x.Recipient, party.Phone, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(x.Recipient, party.Email, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var outstandingInvoices = invoices
                .Where(x => !string.Equals(x.Status, "paid", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var sessionUpdatedAt = await ResolveLastActivityAsync(tenantId, party, ct);
            if (!MatchesParty(filter, party, sessionUpdatedAt)) continue;
            if (!MatchesSales(filter, sales)) continue;
            if (!MatchesInvoices(filter, invoices, outstandingInvoices)) continue;
            if (!MatchesInterest(filter, matchedMessages)) continue;

            var reason = BuildReason(filter, party, sales, outstandingInvoices, matchedMessages);
            contacts.Add(new CampaignAudienceContactContract
            {
                PartyId = party.Id,
                Channel = party.Channel,
                Recipient = party.Phone ?? party.Email ?? party.Identifier,
                DisplayName = party.DisplayName ?? party.FullName ?? party.Identifier,
                Kind = party.Kind,
                PurchaseCount = sales.Count,
                TotalPurchased = sales.Sum(x => x.Total),
                OpenInvoiceCount = outstandingInvoices.Count,
                OutstandingAmount = outstandingInvoices.Sum(x => x.Total),
                Reason = reason
            });
        }

        if (!string.IsNullOrWhiteSpace(campaignId))
        {
            var previousRuns = await _campaignStore.GetRunsAsync(tenantId, campaignId, 50, ct);
            if (previousRuns.Count > 0 && filter.ExcludeCampaignContactDays > 0)
            {
                var threshold = DateTimeOffset.UtcNow.AddDays(-filter.ExcludeCampaignContactDays);
                var recentRecipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var run in previousRuns.Where(x => x.StartedAt >= threshold))
                {
                    var executions = await _campaignStore.GetContactExecutionsAsync(tenantId, run.Id, ct);
                    foreach (var execution in executions)
                        recentRecipients.Add(execution.Recipient);
                }

                contacts = contacts.Where(x => !recentRecipients.Contains(x.Recipient)).ToList();
            }
        }

        return new CampaignAudiencePreviewContract
        {
            EstimatedCount = contacts.Count,
            FilterJson = filterJson,
            Contacts = contacts.Take(50).ToList(),
            Warnings = warnings
        };
    }

    private async Task<DateTimeOffset?> ResolveLastActivityAsync(string tenantId, CommercePartyDocument party, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(party.LastSessionId))
            return party.UpdatedAt;

        var session = await _sessionRepo.GetByIdAsync(party.LastSessionId, tenantId, ct);
        return session?.LastActivityAt ?? party.UpdatedAt;
    }

    private static CampaignAudienceFilter ParseFilter(string filterJson)
    {
        if (string.IsNullOrWhiteSpace(filterJson))
            return new CampaignAudienceFilter();

        try
        {
            return JsonSerializer.Deserialize<CampaignAudienceFilter>(filterJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new CampaignAudienceFilter();
        }
        catch
        {
            return new CampaignAudienceFilter();
        }
    }

    private static bool MatchesParty(CampaignAudienceFilter filter, CommercePartyDocument party, DateTimeOffset? lastActivityAt)
    {
        if (!string.IsNullOrWhiteSpace(filter.Kind) && !string.Equals(filter.Kind, party.Kind, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(filter.Channel) && !string.Equals(filter.Channel, party.Channel, StringComparison.OrdinalIgnoreCase))
            return false;
        if (filter.CreatedAfter.HasValue && party.CreatedAt < filter.CreatedAfter.Value)
            return false;
        if (filter.CreatedBefore.HasValue && party.CreatedAt > filter.CreatedBefore.Value)
            return false;
        if (filter.LastActivityAfter.HasValue && (lastActivityAt is null || lastActivityAt < filter.LastActivityAfter.Value))
            return false;
        if (filter.LastActivityBefore.HasValue && (lastActivityAt is null || lastActivityAt > filter.LastActivityBefore.Value))
            return false;
        return true;
    }

    private static bool MatchesSales(CampaignAudienceFilter filter, IReadOnlyList<CommerceSaleDocument> sales)
    {
        var purchaseCount = sales.Count;
        var totalPurchased = sales.Sum(x => x.Total);
        if (filter.MinPurchaseCount.HasValue && purchaseCount < filter.MinPurchaseCount.Value)
            return false;
        if (filter.MaxPurchaseCount.HasValue && purchaseCount > filter.MaxPurchaseCount.Value)
            return false;
        if (filter.MinTotalPurchased.HasValue && totalPurchased < filter.MinTotalPurchased.Value)
            return false;
        if (filter.MaxTotalPurchased.HasValue && totalPurchased > filter.MaxTotalPurchased.Value)
            return false;
        if (filter.LastPurchaseAfter.HasValue)
        {
            var lastPurchase = sales.OrderByDescending(x => x.CreatedAt).FirstOrDefault()?.CreatedAt;
            if (lastPurchase is null || lastPurchase < filter.LastPurchaseAfter.Value)
                return false;
        }
        return true;
    }

    private static bool MatchesInvoices(
        CampaignAudienceFilter filter,
        IReadOnlyList<CommerceInvoiceDocument> invoices,
        IReadOnlyList<CommerceInvoiceDocument> outstandingInvoices)
    {
        if (!string.IsNullOrWhiteSpace(filter.InvoiceStatus) &&
            !invoices.Any(x => string.Equals(x.Status, filter.InvoiceStatus, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (filter.MinOutstandingAmount.HasValue && outstandingInvoices.Sum(x => x.Total) < filter.MinOutstandingAmount.Value)
            return false;
        if (filter.MinOverdueDays.HasValue)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-filter.MinOverdueDays.Value);
            if (!outstandingInvoices.Any(x => (x.IssuedAt ?? x.CreatedAt) <= cutoff))
                return false;
        }
        if (filter.ExcludePaid && invoices.Any(x => string.Equals(x.Status, "paid", StringComparison.OrdinalIgnoreCase)))
            return false;
        return true;
    }

    private static bool MatchesInterest(CampaignAudienceFilter filter, IReadOnlyList<ConnectInboxMessageContract> messages)
    {
        if ((filter.ProductKeywords is null || filter.ProductKeywords.Count == 0) &&
            (filter.PromotionKeywords is null || filter.PromotionKeywords.Count == 0) &&
            !filter.OnlyFallbackCases)
            return true;

        var allText = string.Join('\n', messages.Select(x => x.Content));
        if (filter.ProductKeywords is { Count: > 0 } &&
            !filter.ProductKeywords.Any(keyword => allText.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (filter.PromotionKeywords is { Count: > 0 } &&
            !filter.PromotionKeywords.Any(keyword => allText.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (filter.OnlyFallbackCases &&
            !messages.Any(x => x.Content.Contains("no_match", StringComparison.OrdinalIgnoreCase)
                            || x.Content.Contains("fallback", StringComparison.OrdinalIgnoreCase)))
            return false;
        return true;
    }

    private static string BuildReason(
        CampaignAudienceFilter filter,
        CommercePartyDocument party,
        IReadOnlyList<CommerceSaleDocument> sales,
        IReadOnlyList<CommerceInvoiceDocument> outstandingInvoices,
        IReadOnlyList<ConnectInboxMessageContract> messages)
    {
        if (outstandingInvoices.Count > 0)
            return $"Tiene {outstandingInvoices.Count} factura(s) abierta(s) por {outstandingInvoices.Sum(x => x.Total):0.##}.";
        if (sales.Count > 0)
            return $"Ha comprado {sales.Count} vez/veces por {sales.Sum(x => x.Total):0.##}.";
        if (filter.ProductKeywords is { Count: > 0 })
            return $"Mostro interes por {string.Join(", ", filter.ProductKeywords)}.";
        if (messages.Count > 0)
            return $"Tiene actividad reciente por {party.Channel}.";
        return "Coincide con los filtros seleccionados.";
    }

    private sealed record CampaignAudienceFilter
    {
        public string? Query { get; init; }
        public string? Kind { get; init; }
        public string? Channel { get; init; }
        public DateTimeOffset? CreatedAfter { get; init; }
        public DateTimeOffset? CreatedBefore { get; init; }
        public DateTimeOffset? LastActivityAfter { get; init; }
        public DateTimeOffset? LastActivityBefore { get; init; }
        public int? MinPurchaseCount { get; init; }
        public int? MaxPurchaseCount { get; init; }
        public decimal? MinTotalPurchased { get; init; }
        public decimal? MaxTotalPurchased { get; init; }
        public DateTimeOffset? LastPurchaseAfter { get; init; }
        public string? InvoiceStatus { get; init; }
        public int? MinOverdueDays { get; init; }
        public decimal? MinOutstandingAmount { get; init; }
        public List<string>? ProductKeywords { get; init; }
        public List<string>? PromotionKeywords { get; init; }
        public bool OnlyFallbackCases { get; init; }
        public bool ExcludePaid { get; init; }
        public int ExcludeCampaignContactDays { get; init; }
    }
}
