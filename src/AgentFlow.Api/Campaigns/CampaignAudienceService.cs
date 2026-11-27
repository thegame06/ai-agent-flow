using System.Text.Json;
using AgentFlow.Abstractions.Connect;
using System.Globalization;
using AgentFlow.Api.Collections;
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

    /// <summary>Full audience (not truncated) used when a campaign actually runs.</summary>
    Task<CampaignAudiencePreviewContract> ResolveAsync(
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
    private readonly ICollectionsStore? _collections;

    /// <summary>Contacts returned by a preview; a run always uses the whole audience.</summary>
    public const int PreviewLimit = 50;

    public CampaignAudienceService(
        ICommerceStore commerce,
        IConnectStore connectStore,
        ICampaignStore campaignStore,
        IChannelSessionRepository sessionRepo,
        ICollectionsStore? collections = null)
    {
        _commerce = commerce;
        _connectStore = connectStore;
        _campaignStore = campaignStore;
        _sessionRepo = sessionRepo;
        _collections = collections;
    }

    public async Task<CampaignAudiencePreviewContract> PreviewAsync(
        string tenantId,
        string filterJson,
        string? campaignId,
        CancellationToken ct = default)
    {
        var full = await ResolveAsync(tenantId, filterJson, campaignId, ct);
        return full with { Contacts = full.Contacts.Take(PreviewLimit).ToList() };
    }

    public async Task<CampaignAudiencePreviewContract> ResolveAsync(
        string tenantId,
        string filterJson,
        string? campaignId,
        CancellationToken ct = default)
    {
        var filter = ParseFilter(filterJson);
        var warnings = new List<string>();
        var contacts = string.Equals(filter.Source, "collections", StringComparison.OrdinalIgnoreCase)
            ? await ResolveCollectionsAsync(tenantId, filter, warnings, ct)
            : await ResolveCommerceAsync(tenantId, filter, warnings, ct);

        contacts = await ExcludeRecentlyContactedAsync(tenantId, campaignId, filter, contacts, ct);
        contacts = contacts
            .Where(c => !string.IsNullOrWhiteSpace(c.Recipient))
            .GroupBy(c => RecipientKeys.From(c.Recipient) ?? c.Recipient, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        return new CampaignAudiencePreviewContract
        {
            EstimatedCount = contacts.Count,
            FilterJson = filterJson,
            Contacts = contacts,
            Warnings = warnings
        };
    }

    private async Task<List<CampaignAudienceContactContract>> ResolveCollectionsAsync(
        string tenantId,
        CampaignAudienceFilter filter,
        List<string> warnings,
        CancellationToken ct)
    {
        if (_collections is null)
        {
            warnings.Add("La cartera de cobranza no esta disponible.");
            return [];
        }

        var stages = (filter.Stages ?? [])
            .Select(s => Enum.TryParse<DelinquencyStage>(s, true, out var st) ? st : (DelinquencyStage?)null)
            .Where(s => s.HasValue).Select(s => s!.Value).ToHashSet();
        var customerIds = (filter.CustomerIds ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var loans = await _collections.GetLoansAsync(tenantId, ct);
        var contacts = new List<CampaignAudienceContactContract>();
        var withoutPhone = 0;
        foreach (var group in loans.GroupBy(l => l.PartyId))
        {
            var open = group.Where(l => l.Status != CollectionLoanStatus.PaidOff &&
                                        (filter.IncludeChargedOff || l.Status != CollectionLoanStatus.ChargedOff)).ToList();
            if (open.Count == 0) continue;

            var first = open[0];
            if (customerIds.Count > 0 && !customerIds.Contains(first.CustomerExternalId ?? string.Empty) && !customerIds.Contains(group.Key)) continue;
            if (!string.IsNullOrWhiteSpace(filter.Query) &&
                !(first.CustomerName ?? string.Empty).Contains(filter.Query, StringComparison.OrdinalIgnoreCase) &&
                !(first.CustomerExternalId ?? string.Empty).Contains(filter.Query, StringComparison.OrdinalIgnoreCase))
                continue;

            var maxDpd = open.Max(l => l.DaysPastDue);
            var overdue = open.Sum(l => l.OverdueBalance);
            var stage = DelinquencyStages.FromDaysPastDue(maxDpd);
            if (stages.Count > 0 && !stages.Contains(stage)) continue;
            if (filter.MinDaysPastDue.HasValue && maxDpd < filter.MinDaysPastDue.Value) continue;
            if (filter.MaxDaysPastDue.HasValue && maxDpd > filter.MaxDaysPastDue.Value) continue;
            if (filter.MinOverdueBalance.HasValue && overdue < filter.MinOverdueBalance.Value) continue;
            if (filter.MinOverdueAmount.HasValue && overdue < filter.MinOverdueAmount.Value) continue;

            var phone = open.Select(l => l.CustomerPhone).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            if (string.IsNullOrWhiteSpace(phone)) { withoutPhone++; continue; }

            var next = open.Where(l => l.NextPaymentDate.HasValue).OrderBy(l => l.NextPaymentDate).FirstOrDefault();
            var name = first.CustomerName ?? first.CustomerExternalId ?? "Cliente";
            var currency = first.Currency ?? string.Empty;
            contacts.Add(new CampaignAudienceContactContract
            {
                PartyId = group.Key,
                Channel = "whatsapp",
                Recipient = phone!,
                DisplayName = name,
                Kind = "customer",
                OpenInvoiceCount = open.Count,
                OutstandingAmount = overdue,
                Reason = $"{open.Count} prestamo(s), {maxDpd} dias de atraso, vencido {Money(overdue)} {currency}".Trim(),
                Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nombre"] = name,
                    ["primer_nombre"] = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name,
                    ["cliente_id"] = first.CustomerExternalId ?? group.Key,
                    ["total_vencido"] = Money(overdue),
                    ["saldo_total"] = Money(open.Sum(l => l.TotalBalance)),
                    ["dias_atraso"] = maxDpd.ToString(CultureInfo.InvariantCulture),
                    ["etapa"] = DelinquencyStages.Label(stage),
                    ["num_prestamos"] = open.Count.ToString(CultureInfo.InvariantCulture),
                    ["prestamos"] = string.Join(", ", open.Select(l => l.LoanExternalId)),
                    ["moneda"] = currency,
                    ["proximo_pago_fecha"] = next?.NextPaymentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
                    ["proximo_pago_monto"] = next?.NextPaymentAmount is { } amount ? Money(amount) : string.Empty
                }
            });
        }

        if (withoutPhone > 0)
            warnings.Add($"{withoutPhone} cliente(s) coinciden pero no tienen telefono y se omiten.");
        return contacts.OrderByDescending(c => c.OutstandingAmount).ToList();
    }

    private static string Money(decimal value) => value.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private async Task<List<CampaignAudienceContactContract>> ExcludeRecentlyContactedAsync(
        string tenantId,
        string? campaignId,
        CampaignAudienceFilter filter,
        List<CampaignAudienceContactContract> contacts,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(campaignId) || filter.ExcludeCampaignContactDays <= 0)
            return contacts;

        var threshold = DateTimeOffset.UtcNow.AddDays(-filter.ExcludeCampaignContactDays);
        var recent = (await _campaignStore.GetContactExecutionsByCampaignAsync(tenantId, campaignId, 20000, ct))
            .Where(x => x.CreatedAt >= threshold)
            .Select(x => RecipientKeys.From(x.Recipient) ?? x.Recipient)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return contacts.Where(x => !recent.Contains(RecipientKeys.From(x.Recipient) ?? x.Recipient)).ToList();
    }

    private async Task<List<CampaignAudienceContactContract>> ResolveCommerceAsync(
        string tenantId,
        CampaignAudienceFilter filter,
        List<string> warnings,
        CancellationToken ct)
    {
        var parties = await _commerce.SearchPartiesAsync(tenantId, filter.Query, 0, 1000, ct);
        var inbox = await _connectStore.GetInboxAsync(tenantId, 5000, ct);
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
            var displayName = party.DisplayName ?? party.FullName ?? party.Identifier;
            contacts.Add(new CampaignAudienceContactContract
            {
                Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nombre"] = displayName,
                    ["primer_nombre"] = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? displayName,
                    ["compras"] = sales.Count.ToString(CultureInfo.InvariantCulture),
                    ["total_comprado"] = Money(sales.Sum(x => x.Total)),
                    ["saldo_pendiente"] = Money(outstandingInvoices.Sum(x => x.Total))
                },
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

        return contacts;
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
        /// <summary>"commerce" (default: customers and leads) or "collections" (loan portfolio).</summary>
        public string? Source { get; init; }
        public List<string>? Stages { get; init; }
        public int? MinDaysPastDue { get; init; }
        public int? MaxDaysPastDue { get; init; }
        public decimal? MinOverdueBalance { get; init; }
        public decimal? MinOverdueAmount { get; init; }
        public bool IncludeChargedOff { get; init; }
        public List<string>? CustomerIds { get; init; }
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
