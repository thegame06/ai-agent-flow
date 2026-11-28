using AgentFlow.Api.Collections;

namespace AgentFlow.Api.Commerce;

public sealed record SaleLine
{
    public required string Sku { get; init; }
    public decimal Quantity { get; init; } = 1;
    /// <summary>Price agreed for this line; the catalog price when empty.</summary>
    public decimal? UnitPrice { get; init; }
    public string? Name { get; init; }
}

public sealed record NewSale
{
    public required string TenantId { get; init; }
    public required string PartyId { get; init; }
    public required IReadOnlyList<SaleLine> Lines { get; init; }
    /// <summary>cash | card | transfer | installments</summary>
    public string PaymentMethod { get; init; } = "cash";
    public InstallmentPlanRequest? Installments { get; init; }
    public string? Currency { get; init; }
    public string? SessionId { get; init; }
    public string? ThreadId { get; init; }
    public decimal? DiscountAmount { get; init; }
    public decimal? DiscountPercent { get; init; }
    /// <summary>Whether to charge tax; the store's rate decides when empty (no tax at 0%).</summary>
    public bool? ApplyTax { get; init; }
    public decimal? TaxRate { get; init; }
}

public enum SaleError { None, PartyNotFound, SkuNotFound, SkuInactive, InsufficientStock, InvalidPlan, InvalidLine }

public sealed record SaleResult(CommerceSaleDocument? Sale, SaleError Error = SaleError.None, string? Message = null, string? Sku = null)
{
    public bool Ok => Error == SaleError.None && Sale is not null;
}

public sealed record SaleQuoteLine(string Sku, string Name, string ItemType, decimal UnitPrice, decimal Quantity, decimal Subtotal, int? OnHand);
public sealed record SaleQuote(IReadOnlyList<SaleQuoteLine> Lines, decimal Subtotal, decimal Discount, decimal Tax, decimal Total, string Currency, string? Error = null, string? Sku = null);

public interface ISalesService
{
    /// <summary>Prices the lines with the catalog (and the store's tax), without selling.</summary>
    Task<SaleQuote> QuoteAsync(string tenantId, IReadOnlyList<SaleLine> lines, CancellationToken ct);
    /// <summary>
    /// Sells: checks the catalog and stock, prices, records the sale, takes the stock out and, for a
    /// sale on installments, opens the customer's account in collections with its schedule.
    /// </summary>
    Task<SaleResult> CreateAsync(NewSale sale, CancellationToken ct);
}

public sealed class SalesService : ISalesService
{
    public const string Installments = "installments";
    private readonly ICommerceStore _commerce;
    private readonly ICollectionsStore? _collections;
    private readonly ILogger<SalesService> _logger;

    public SalesService(ICommerceStore commerce, ILogger<SalesService> logger, ICollectionsStore? collections = null)
    {
        _commerce = commerce;
        _collections = collections;
        _logger = logger;
    }

    public async Task<SaleQuote> QuoteAsync(string tenantId, IReadOnlyList<SaleLine> lines, CancellationToken ct)
    {
        var settings = await _commerce.GetStoreSettingsAsync(tenantId, ct);
        var priced = new List<SaleQuoteLine>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                return Empty(settings.Currency, $"La cantidad de {line.Sku} debe ser mayor que cero.", line.Sku);
            var item = await _commerce.GetInventoryBySkuAsync(tenantId, line.Sku.Trim(), ct);
            if (item is null) return Empty(settings.Currency, $"No hay ningún producto o servicio con código {line.Sku}.", line.Sku);
            if (!item.Active) return Empty(settings.Currency, $"{item.Name} no está disponible.", line.Sku);
            var price = line.UnitPrice ?? item.UnitPrice;
            priced.Add(new SaleQuoteLine(item.Sku, item.Name, item.ItemType, price, line.Quantity, price * line.Quantity,
                item.TracksInventory ? item.OnHand : null));
        }

        var calc = await _commerce.CalculateSaleAsync(tenantId,
            priced.Select(l => new CommerceLineItem { Sku = l.Sku, Name = l.Name, UnitPrice = l.UnitPrice, Quantity = l.Quantity }).ToList(),
            null, null, settings.TaxRate > 0, TaxFraction(settings.TaxRate), ct);
        return new SaleQuote(priced, calc.Subtotal, calc.Discount, calc.Tax, calc.Total, settings.Currency);

        static SaleQuote Empty(string currency, string error, string sku) => new([], 0, 0, 0, 0, currency, error, sku);
    }

    public async Task<SaleResult> CreateAsync(NewSale request, CancellationToken ct)
    {
        if (request.Lines.Count == 0 || request.Lines.Any(l => string.IsNullOrWhiteSpace(l.Sku) || l.Quantity <= 0))
            return new(null, SaleError.InvalidLine, "La venta necesita al menos un producto con cantidad mayor que cero.");

        var party = await _commerce.GetPartyByIdAsync(request.TenantId, request.PartyId, ct);
        if (party is null) return new(null, SaleError.PartyNotFound, $"Party '{request.PartyId}' not found.");

        var items = new List<(SaleLine Line, CommerceInventoryItemDocument Item)>();
        foreach (var line in request.Lines)
        {
            var item = await _commerce.GetInventoryBySkuAsync(request.TenantId, line.Sku.Trim(), ct);
            if (item is null) return new(null, SaleError.SkuNotFound, $"Inventory SKU '{line.Sku}' not found.", line.Sku);
            if (!item.Active) return new(null, SaleError.SkuInactive, $"Inventory SKU '{line.Sku}' is inactive.", line.Sku);
            if (item.TracksInventory && item.OnHand < (int)Math.Ceiling(line.Quantity))
                return new(null, SaleError.InsufficientStock, $"Insufficient stock for SKU '{line.Sku}'.", line.Sku);
            items.Add((line, item));
        }

        var settings = await _commerce.GetStoreSettingsAsync(request.TenantId, ct);
        var lineItems = items.Select(x => new CommerceLineItem
        {
            Sku = x.Item.Sku,
            Name = string.IsNullOrWhiteSpace(x.Line.Name) ? x.Item.Name : x.Line.Name!,
            UnitPrice = x.Line.UnitPrice ?? x.Item.UnitPrice,
            Quantity = x.Line.Quantity
        }).ToList();
        var taxRate = TaxFraction(request.TaxRate ?? settings.TaxRate);
        var calc = await _commerce.CalculateSaleAsync(request.TenantId, lineItems, request.DiscountAmount, request.DiscountPercent,
            request.ApplyTax ?? taxRate > 0, taxRate, ct);

        var method = NormalizeMethod(request.PaymentMethod);
        if (method == Installments && InstallmentPlanner.Validate(request.Installments, calc.Total) is { } planError)
            return new(null, SaleError.InvalidPlan, planError);

        var sale = new CommerceSaleDocument
        {
            TenantId = request.TenantId,
            PartyId = party.Id,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? settings.Currency : request.Currency.Trim().ToUpperInvariant(),
            PaymentMethod = method,
            SessionId = request.SessionId,
            ThreadId = request.ThreadId,
            Items = lineItems,
            Subtotal = calc.Subtotal,
            Discount = calc.Discount,
            Tax = calc.Tax,
            Total = calc.Total,
            State = method == Installments ? "financed" : "sale_created"
        };

        if (method == Installments)
        {
            var plan = request.Installments! with { Frequency = InstallmentPlanner.Normalize(request.Installments!.Frequency) };
            sale.Financing = new SaleFinancingDocument
            {
                DownPayment = plan.DownPayment,
                FinancedAmount = calc.Total - plan.DownPayment,
                Count = plan.Count,
                Frequency = plan.Frequency,
                Installments = InstallmentPlanner.Build(calc.Total, plan, DateTimeOffset.UtcNow)
            };
        }

        var saved = await _commerce.CreateSaleAsync(sale, ct);
        foreach (var (line, item) in items.Where(x => x.Item.TracksInventory))
            await _commerce.AdjustInventoryAsync(request.TenantId, item.Sku, -(int)Math.Ceiling(line.Quantity), "sale_created", saved.Id, ct);

        if (saved.Financing is not null)
            await OpenCollectionsAccountAsync(saved, party, ct);
        return new(saved);
    }

    /// <summary>
    /// The financed balance becomes an account in collections, so the customer gets the same
    /// reminders, payment reports, promises and assistant as any other account.
    /// </summary>
    private async Task OpenCollectionsAccountAsync(CommerceSaleDocument sale, CommercePartyDocument party, CancellationToken ct)
    {
        if (_collections is null) return;
        var financing = sale.Financing!;
        var now = DateTimeOffset.UtcNow;
        var loan = new CollectionLoanDocument
        {
            TenantId = sale.TenantId,
            LoanExternalId = LoanExternalIdFor(sale.Id),
            PartyId = party.Id,
            CustomerName = party.FullName ?? party.DisplayName,
            CustomerPhone = party.Phone,
            Product = string.Join(", ", sale.Items.Select(i => i.Name)),
            Currency = sale.Currency,
            OriginalAmount = financing.FinancedAmount,
            DisbursedAt = now,
            PaymentFrequency = financing.Frequency,
            Installments = financing.Installments.Select(i => new CollectionInstallment
            {
                Number = i.Number, DueDate = i.DueDate, Amount = i.Amount, PaidAmount = i.PaidAmount, Status = i.Status
            }).ToList(),
            Status = CollectionLoanStatus.Active,
            Origin = "sale",
            SaleId = sale.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        InstallmentSchedule.Age(loan, now);

        try
        {
            await _collections.UpsertLoanAsync(loan, ct);
            await _collections.UpsertSnapshotAsync(new CollectionLoanSnapshotDocument
            {
                Id = $"{loan.Id}:{now:yyyyMMdd}",
                TenantId = loan.TenantId,
                LoanId = loan.Id,
                SnapshotDate = now,
                DaysPastDue = loan.DaysPastDue,
                Stage = loan.Stage,
                TotalBalance = loan.TotalBalance,
                OverdueBalance = loan.OverdueBalance,
                Status = loan.Status
            }, ct);
            financing.CollectionLoanId = loan.Id;
            financing.LoanExternalId = loan.LoanExternalId;
            await _commerce.UpdateSaleAsync(sale, ct);
        }
        catch (Exception ex)
        {
            // The sale stands; the account can be opened again from the sale.
            _logger.LogError(ex, "Could not open the collections account for sale {SaleId}", sale.Id);
        }
    }

    /// <summary>The store's tax rate as a fraction: "15" typed as a percentage is 0.15.</summary>
    public static decimal TaxFraction(decimal rate) => rate > 1 ? rate / 100m : Math.Max(0, rate);

    public static string LoanExternalIdFor(string saleId) => $"VENTA-{saleId[..Math.Min(8, saleId.Length)].ToUpperInvariant()}";

    /// <summary>"contado"/"efectivo" are cash; "cuotas"/"credito"/"en pagos" are installments.</summary>
    public static string NormalizeMethod(string? method) => (method ?? "cash").Trim().ToLowerInvariant() switch
    {
        "" or "cash" or "contado" or "efectivo" => "cash",
        "installments" or "cuotas" or "credito" or "crédito" or "en pagos" or "financed" => Installments,
        "card" or "tarjeta" => "card",
        "transfer" or "transferencia" => "transfer",
        var other => other
    };
}
