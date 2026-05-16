using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace AgentFlow.Api.Commerce;

public interface ICommerceStore
{
    Task<CommercePartyDocument> UpsertPartyByChannelIdentityAsync(
        string tenantId,
        string channel,
        string identifier,
        string? displayName,
        string kind,
        string? sourceSessionId,
        string? sourceThreadId,
        string? phone,
        string? email,
        string? fullName,
        CancellationToken ct);
    Task<CommercePartyDocument?> GetPartyByIdAsync(string tenantId, string partyId, CancellationToken ct);
    Task<CommercePartyDocument?> GetPartyByIdentityAsync(string tenantId, string channel, string identifier, CancellationToken ct);
    Task<IReadOnlyList<CommercePartyDocument>> SearchPartiesAsync(string tenantId, string? query, int page, int pageSize, CancellationToken ct);
    Task<long> CountPartiesAsync(string tenantId, string? query, CancellationToken ct);
    Task<CommercePartyDocument?> UpdatePartyAsync(string tenantId, string partyId, string? fullName, string? email, string? phone, string? displayName, string? kind, CancellationToken ct);
    Task<bool> DeletePartyAsync(string tenantId, string partyId, CancellationToken ct);
    Task<IReadOnlyList<CommerceInventoryItemDocument>> SearchInventoryAsync(string tenantId, string? query, int limit, CancellationToken ct);
    Task<CommerceInventoryItemDocument?> GetInventoryBySkuAsync(string tenantId, string sku, CancellationToken ct);
    Task<CommerceInventoryItemDocument> UpsertInventoryItemAsync(string tenantId, string sku, string name, decimal unitPrice, int onHand, bool active, CancellationToken ct);
    Task<CommerceInventoryItemDocument> AdjustInventoryAsync(string tenantId, string sku, int delta, string reason, string? referenceId, CancellationToken ct);
    Task<IReadOnlyList<CommerceInventoryMovementDocument>> SearchInventoryMovementsAsync(string tenantId, string? sku, int page, int pageSize, CancellationToken ct);
    Task<long> CountInventoryMovementsAsync(string tenantId, string? sku, CancellationToken ct);
    Task<CommerceSaleDocument> CreateSaleAsync(CommerceSaleDocument sale, CancellationToken ct);
    Task<CommerceSaleDocument?> GetSaleByIdAsync(string tenantId, string saleId, CancellationToken ct);
    Task<IReadOnlyList<CommerceSaleDocument>> SearchSalesAsync(string tenantId, string? partyId, string? state, int page, int pageSize, CancellationToken ct);
    Task<long> CountSalesAsync(string tenantId, string? partyId, string? state, CancellationToken ct);
    Task<CommerceSaleDocument> UpdateSaleAsync(CommerceSaleDocument sale, CancellationToken ct);
    Task<SaleCalculationResult> CalculateSaleAsync(string tenantId, IReadOnlyList<CommerceLineItem> items, decimal? discountAmount, decimal? discountPercent, bool applyTax, decimal taxRate, CancellationToken ct);
    Task<CommerceOrderDocument> CreateOrderAsync(CommerceOrderDocument order, CancellationToken ct);
    Task<CommerceInvoiceDocument> CreateInvoiceAsync(CommerceInvoiceDocument invoice, CancellationToken ct);
    Task<CommerceInvoiceDocument?> GetInvoiceByIdAsync(string tenantId, string invoiceId, CancellationToken ct);
    Task<IReadOnlyList<CommerceInvoiceDocument>> SearchInvoicesAsync(string tenantId, string? partyId, string? status, int page, int pageSize, CancellationToken ct);
    Task<long> CountInvoicesAsync(string tenantId, string? partyId, string? status, CancellationToken ct);
    Task<CommerceInvoiceDocument> UpdateInvoiceAsync(CommerceInvoiceDocument invoice, CancellationToken ct);
    Task<CommerceInvoiceDocument> UpdateInvoiceStatusAsync(string tenantId, string invoiceId, string status, CancellationToken ct);
}

public sealed class CommerceStore : ICommerceStore
{
    private readonly IMongoCollection<CommercePartyDocument> _parties;
    private readonly IMongoCollection<CommerceSaleDocument> _sales;
    private readonly IMongoCollection<CommerceOrderDocument> _orders;
    private readonly IMongoCollection<CommerceInvoiceDocument> _invoices;
    private readonly IMongoCollection<CommerceInventoryItemDocument> _inventory;
    private readonly IMongoCollection<CommerceInventoryMovementDocument> _inventoryMovements;

    public CommerceStore(IMongoDatabase database)
    {
        _parties = database.GetCollection<CommercePartyDocument>("commerce_parties");
        _sales = database.GetCollection<CommerceSaleDocument>("commerce_sales");
        _orders = database.GetCollection<CommerceOrderDocument>("commerce_orders");
        _invoices = database.GetCollection<CommerceInvoiceDocument>("commerce_invoices");
        _inventory = database.GetCollection<CommerceInventoryItemDocument>("commerce_inventory_items");
        _inventoryMovements = database.GetCollection<CommerceInventoryMovementDocument>("commerce_inventory_movements");
    }

    public async Task<CommercePartyDocument> UpsertPartyByChannelIdentityAsync(
        string tenantId,
        string channel,
        string identifier,
        string? displayName,
        string kind,
        string? sourceSessionId,
        string? sourceThreadId,
        string? phone,
        string? email,
        string? fullName,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var normalizedPhone = NormalizePhone(phone);

        if (!string.IsNullOrWhiteSpace(normalizedPhone))
        {
            var byPhone = await _parties.Find(x => x.TenantId == tenantId && x.Phone == normalizedPhone).FirstOrDefaultAsync(ct);
            if (byPhone is not null)
            {
                UpsertIdentity(byPhone, channel, identifier);
                byPhone.DisplayName = string.IsNullOrWhiteSpace(displayName) ? byPhone.DisplayName : displayName;
                byPhone.FullName = string.IsNullOrWhiteSpace(fullName) ? byPhone.FullName : fullName;
                byPhone.Email = string.IsNullOrWhiteSpace(email) ? byPhone.Email : email;
                byPhone.Phone = normalizedPhone;
                byPhone.Kind = PromoteKind(byPhone.Kind, kind);
                byPhone.LastSessionId = sourceSessionId ?? byPhone.LastSessionId;
                byPhone.LastThreadId = sourceThreadId ?? byPhone.LastThreadId;
                byPhone.UpdatedAt = now;

                await _parties.ReplaceOneAsync(x => x.Id == byPhone.Id && x.TenantId == tenantId, byPhone, cancellationToken: ct);
                return byPhone;
            }
        }

        var filter = Builders<CommercePartyDocument>.Filter.And(
            Builders<CommercePartyDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<CommercePartyDocument>.Filter.Eq(x => x.Channel, channel),
            Builders<CommercePartyDocument>.Filter.Eq(x => x.Identifier, identifier));

        var current = await _parties.Find(filter).FirstOrDefaultAsync(ct);
        if (current is null)
        {
            var created = new CommercePartyDocument
            {
                TenantId = tenantId,
                Channel = channel,
                Identifier = identifier,
                DisplayName = displayName,
                FullName = fullName,
                Email = email,
                Phone = normalizedPhone,
                Kind = string.IsNullOrWhiteSpace(kind) ? "lead" : kind,
                LastSessionId = sourceSessionId,
                LastThreadId = sourceThreadId,
                LinkedIdentities =
                [
                    new CommerceIdentityLink { Channel = channel, Identifier = identifier }
                ],
                CreatedAt = now,
                UpdatedAt = now
            };
            await _parties.InsertOneAsync(created, cancellationToken: ct);
            return created;
        }

        current.DisplayName = string.IsNullOrWhiteSpace(displayName) ? current.DisplayName : displayName;
        current.Kind = PromoteKind(current.Kind, kind);
        current.FullName = string.IsNullOrWhiteSpace(fullName) ? current.FullName : fullName;
        current.Email = string.IsNullOrWhiteSpace(email) ? current.Email : email;
        current.Phone = string.IsNullOrWhiteSpace(normalizedPhone) ? current.Phone : normalizedPhone;
        current.LastSessionId = sourceSessionId ?? current.LastSessionId;
        current.LastThreadId = sourceThreadId ?? current.LastThreadId;
        UpsertIdentity(current, channel, identifier);
        current.UpdatedAt = now;

        await _parties.ReplaceOneAsync(filter, current, cancellationToken: ct);
        return current;
    }

    public async Task<CommercePartyDocument?> GetPartyByIdAsync(string tenantId, string partyId, CancellationToken ct)
    {
        return await _parties.Find(x => x.TenantId == tenantId && x.Id == partyId).FirstOrDefaultAsync(ct);
    }

    public async Task<CommercePartyDocument?> GetPartyByIdentityAsync(string tenantId, string channel, string identifier, CancellationToken ct)
    {
        return await _parties.Find(x =>
                x.TenantId == tenantId &&
                (x.Channel == channel && x.Identifier == identifier ||
                 x.LinkedIdentities.Any(l => l.Channel == channel && l.Identifier == identifier)))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<CommercePartyDocument>> SearchPartiesAsync(string tenantId, string? query, int page, int pageSize, CancellationToken ct)
    {
        var filter = BuildPartySearchFilter(tenantId, query);
        return await _parties.Find(filter)
            .SortByDescending(x => x.UpdatedAt)
            .Skip(Math.Max(0, page) * Math.Clamp(pageSize, 1, 100))
            .Limit(Math.Clamp(pageSize, 1, 100))
            .ToListAsync(ct);
    }

    public async Task<long> CountPartiesAsync(string tenantId, string? query, CancellationToken ct)
    {
        var filter = BuildPartySearchFilter(tenantId, query);
        return await _parties.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task<CommercePartyDocument?> UpdatePartyAsync(
        string tenantId,
        string partyId,
        string? fullName,
        string? email,
        string? phone,
        string? displayName,
        string? kind,
        CancellationToken ct)
    {
        var current = await _parties.Find(x => x.TenantId == tenantId && x.Id == partyId).FirstOrDefaultAsync(ct);
        if (current is null) return null;
        var normalizedPhone = NormalizePhone(phone);
        if (!string.IsNullOrWhiteSpace(normalizedPhone) && !string.Equals(current.Phone, normalizedPhone, StringComparison.OrdinalIgnoreCase))
        {
            var byPhone = await _parties.Find(x => x.TenantId == tenantId && x.Phone == normalizedPhone && x.Id != partyId).FirstOrDefaultAsync(ct);
            if (byPhone is not null)
            {
                throw new InvalidOperationException("Phone already belongs to another customer/lead.");
            }
        }

        current.FullName = string.IsNullOrWhiteSpace(fullName) ? current.FullName : fullName;
        current.Email = string.IsNullOrWhiteSpace(email) ? current.Email : email;
        current.DisplayName = string.IsNullOrWhiteSpace(displayName) ? current.DisplayName : displayName;
        current.Phone = string.IsNullOrWhiteSpace(normalizedPhone) ? current.Phone : normalizedPhone;
        current.Kind = PromoteKind(current.Kind, kind);
        current.UpdatedAt = DateTimeOffset.UtcNow;
        await _parties.ReplaceOneAsync(x => x.TenantId == tenantId && x.Id == partyId, current, cancellationToken: ct);
        return current;
    }

    public async Task<bool> DeletePartyAsync(string tenantId, string partyId, CancellationToken ct)
    {
        var result = await _parties.DeleteOneAsync(x => x.TenantId == tenantId && x.Id == partyId, ct);
        return result.DeletedCount > 0;
    }

    public async Task<CommerceInventoryItemDocument?> FindInventoryItemAsync(string tenantId, string sku, CancellationToken ct)
    {
        return await _inventory.Find(x => x.TenantId == tenantId && x.Sku == sku).FirstOrDefaultAsync(ct);
    }

    public Task<CommerceInventoryItemDocument?> GetInventoryBySkuAsync(string tenantId, string sku, CancellationToken ct)
        => FindInventoryItemAsync(tenantId, sku, ct);

    public async Task<IReadOnlyList<CommerceInventoryItemDocument>> SearchInventoryAsync(string tenantId, string? query, int limit, CancellationToken ct)
    {
        var bounded = Math.Clamp(limit, 1, 100);
        var filter = Builders<CommerceInventoryItemDocument>.Filter.Eq(x => x.TenantId, tenantId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            filter = Builders<CommerceInventoryItemDocument>.Filter.And(
                filter,
                Builders<CommerceInventoryItemDocument>.Filter.Or(
                    Builders<CommerceInventoryItemDocument>.Filter.Regex(x => x.Sku, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                    Builders<CommerceInventoryItemDocument>.Filter.Regex(x => x.Name, new MongoDB.Bson.BsonRegularExpression(q, "i"))));
        }

        return await _inventory.Find(filter).Limit(bounded).ToListAsync(ct);
    }

    public async Task<CommerceInventoryItemDocument> UpsertInventoryItemAsync(
        string tenantId,
        string sku,
        string name,
        decimal unitPrice,
        int onHand,
        bool active,
        CancellationToken ct)
    {
        var current = await _inventory.Find(x => x.TenantId == tenantId && x.Sku == sku).FirstOrDefaultAsync(ct);
        if (current is null)
        {
            var created = new CommerceInventoryItemDocument
            {
                TenantId = tenantId,
                Sku = sku,
                Name = name,
                UnitPrice = unitPrice,
                OnHand = onHand,
                Active = active
            };
            await _inventory.InsertOneAsync(created, cancellationToken: ct);
            return created;
        }

        current.Name = name;
        current.UnitPrice = unitPrice;
        current.OnHand = onHand;
        current.Active = active;
        await _inventory.ReplaceOneAsync(x => x.TenantId == tenantId && x.Sku == sku, current, cancellationToken: ct);
        return current;
    }

    public async Task<CommerceInventoryItemDocument> AdjustInventoryAsync(string tenantId, string sku, int delta, string reason, string? referenceId, CancellationToken ct)
    {
        var current = await _inventory.Find(x => x.TenantId == tenantId && x.Sku == sku).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Inventory SKU '{sku}' not found.");
        var next = current.OnHand + delta;
        if (next < 0)
            throw new InvalidOperationException($"Insufficient stock for SKU '{sku}'.");
        current.OnHand = next;
        await _inventory.ReplaceOneAsync(x => x.TenantId == tenantId && x.Sku == sku, current, cancellationToken: ct);

        await _inventoryMovements.InsertOneAsync(new CommerceInventoryMovementDocument
        {
            TenantId = tenantId,
            Sku = sku,
            Delta = delta,
            Balance = next,
            Reason = reason,
            ReferenceId = referenceId,
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken: ct);

        return current;
    }

    public async Task<IReadOnlyList<CommerceInventoryMovementDocument>> SearchInventoryMovementsAsync(string tenantId, string? sku, int page, int pageSize, CancellationToken ct)
    {
        var filter = Builders<CommerceInventoryMovementDocument>.Filter.Eq(x => x.TenantId, tenantId);
        if (!string.IsNullOrWhiteSpace(sku))
            filter = Builders<CommerceInventoryMovementDocument>.Filter.And(filter, Builders<CommerceInventoryMovementDocument>.Filter.Eq(x => x.Sku, sku));
        return await _inventoryMovements.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Skip(Math.Max(0, page) * Math.Clamp(pageSize, 1, 100))
            .Limit(Math.Clamp(pageSize, 1, 100))
            .ToListAsync(ct);
    }

    public async Task<long> CountInventoryMovementsAsync(string tenantId, string? sku, CancellationToken ct)
    {
        var filter = Builders<CommerceInventoryMovementDocument>.Filter.Eq(x => x.TenantId, tenantId);
        if (!string.IsNullOrWhiteSpace(sku))
            filter = Builders<CommerceInventoryMovementDocument>.Filter.And(filter, Builders<CommerceInventoryMovementDocument>.Filter.Eq(x => x.Sku, sku));
        return await _inventoryMovements.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task<CommerceSaleDocument> CreateSaleAsync(CommerceSaleDocument sale, CancellationToken ct)
    {
        sale.CreatedAt = DateTimeOffset.UtcNow;
        await _sales.InsertOneAsync(sale, cancellationToken: ct);
        return sale;
    }

    public async Task<CommerceSaleDocument?> GetSaleByIdAsync(string tenantId, string saleId, CancellationToken ct)
        => await _sales.Find(x => x.TenantId == tenantId && x.Id == saleId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CommerceSaleDocument>> SearchSalesAsync(string tenantId, string? partyId, string? state, int page, int pageSize, CancellationToken ct)
    {
        var filter = BuildSaleSearchFilter(tenantId, partyId, state);
        return await _sales.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Skip(Math.Max(0, page) * Math.Clamp(pageSize, 1, 100))
            .Limit(Math.Clamp(pageSize, 1, 100))
            .ToListAsync(ct);
    }

    public async Task<long> CountSalesAsync(string tenantId, string? partyId, string? state, CancellationToken ct)
    {
        var filter = BuildSaleSearchFilter(tenantId, partyId, state);
        return await _sales.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task<CommerceSaleDocument> UpdateSaleAsync(CommerceSaleDocument sale, CancellationToken ct)
    {
        await _sales.ReplaceOneAsync(x => x.TenantId == sale.TenantId && x.Id == sale.Id, sale, cancellationToken: ct);
        return sale;
    }

    public Task<SaleCalculationResult> CalculateSaleAsync(
        string tenantId,
        IReadOnlyList<CommerceLineItem> items,
        decimal? discountAmount,
        decimal? discountPercent,
        bool applyTax,
        decimal taxRate,
        CancellationToken ct)
    {
        var subtotal = items.Sum(i => i.Subtotal);
        var percentDiscount = (discountPercent ?? 0m) > 0 ? subtotal * (discountPercent!.Value / 100m) : 0m;
        var discount = Math.Max(0m, discountAmount ?? 0m) + Math.Max(0m, percentDiscount);
        if (discount > subtotal) discount = subtotal;
        var taxable = subtotal - discount;
        var tax = applyTax ? Math.Round(taxable * Math.Max(0m, taxRate), 2) : 0m;
        var total = taxable + tax;
        return Task.FromResult(new SaleCalculationResult(subtotal, discount, tax, total));
    }

    public async Task<CommerceOrderDocument> CreateOrderAsync(CommerceOrderDocument order, CancellationToken ct)
    {
        order.CreatedAt = DateTimeOffset.UtcNow;
        await _orders.InsertOneAsync(order, cancellationToken: ct);
        return order;
    }

    public async Task<CommerceInvoiceDocument> CreateInvoiceAsync(CommerceInvoiceDocument invoice, CancellationToken ct)
    {
        invoice.CreatedAt = DateTimeOffset.UtcNow;
        await _invoices.InsertOneAsync(invoice, cancellationToken: ct);
        return invoice;
    }

    public async Task<CommerceInvoiceDocument?> GetInvoiceByIdAsync(string tenantId, string invoiceId, CancellationToken ct)
        => await _invoices.Find(x => x.TenantId == tenantId && x.Id == invoiceId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CommerceInvoiceDocument>> SearchInvoicesAsync(string tenantId, string? partyId, string? status, int page, int pageSize, CancellationToken ct)
    {
        var filter = BuildInvoiceSearchFilter(tenantId, partyId, status);
        return await _invoices.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Skip(Math.Max(0, page) * Math.Clamp(pageSize, 1, 100))
            .Limit(Math.Clamp(pageSize, 1, 100))
            .ToListAsync(ct);
    }

    public async Task<long> CountInvoicesAsync(string tenantId, string? partyId, string? status, CancellationToken ct)
    {
        var filter = BuildInvoiceSearchFilter(tenantId, partyId, status);
        return await _invoices.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task<CommerceInvoiceDocument> UpdateInvoiceAsync(CommerceInvoiceDocument invoice, CancellationToken ct)
    {
        await _invoices.ReplaceOneAsync(x => x.TenantId == invoice.TenantId && x.Id == invoice.Id, invoice, cancellationToken: ct);
        return invoice;
    }

    public async Task<CommerceInvoiceDocument> UpdateInvoiceStatusAsync(string tenantId, string invoiceId, string status, CancellationToken ct)
    {
        var current = await _invoices.Find(x => x.TenantId == tenantId && x.Id == invoiceId).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Invoice '{invoiceId}' not found.");
        current.Status = status;
        return await UpdateInvoiceAsync(current, ct);
    }

    private static FilterDefinition<CommercePartyDocument> BuildPartySearchFilter(string tenantId, string? query)
    {
        var filter = Builders<CommercePartyDocument>.Filter.Eq(x => x.TenantId, tenantId);
        if (string.IsNullOrWhiteSpace(query)) return filter;
        var q = query.Trim();
        return Builders<CommercePartyDocument>.Filter.And(
            filter,
            Builders<CommercePartyDocument>.Filter.Or(
                Builders<CommercePartyDocument>.Filter.Regex(x => x.Identifier, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                Builders<CommercePartyDocument>.Filter.Regex(x => x.DisplayName, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                Builders<CommercePartyDocument>.Filter.Regex(x => x.FullName, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                Builders<CommercePartyDocument>.Filter.Regex(x => x.Email, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                Builders<CommercePartyDocument>.Filter.Regex(x => x.Phone, new MongoDB.Bson.BsonRegularExpression(q, "i"))));
    }

    private static FilterDefinition<CommerceSaleDocument> BuildSaleSearchFilter(string tenantId, string? partyId, string? state)
    {
        var filter = Builders<CommerceSaleDocument>.Filter.Eq(x => x.TenantId, tenantId);
        if (!string.IsNullOrWhiteSpace(partyId))
            filter = Builders<CommerceSaleDocument>.Filter.And(filter, Builders<CommerceSaleDocument>.Filter.Eq(x => x.PartyId, partyId));
        if (!string.IsNullOrWhiteSpace(state))
            filter = Builders<CommerceSaleDocument>.Filter.And(filter, Builders<CommerceSaleDocument>.Filter.Eq(x => x.State, state));
        return filter;
    }

    private static FilterDefinition<CommerceInvoiceDocument> BuildInvoiceSearchFilter(string tenantId, string? partyId, string? status)
    {
        var filter = Builders<CommerceInvoiceDocument>.Filter.Eq(x => x.TenantId, tenantId);
        if (!string.IsNullOrWhiteSpace(partyId))
            filter = Builders<CommerceInvoiceDocument>.Filter.And(filter, Builders<CommerceInvoiceDocument>.Filter.Eq(x => x.PartyId, partyId));
        if (!string.IsNullOrWhiteSpace(status))
            filter = Builders<CommerceInvoiceDocument>.Filter.And(filter, Builders<CommerceInvoiceDocument>.Filter.Eq(x => x.Status, status));
        return filter;
    }

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var chars = phone.Where(c => char.IsDigit(c) || c == '+').ToArray();
        var normalized = new string(chars);
        if (normalized.Count(char.IsDigit) < 7) return null;
        return normalized;
    }

    private static string PromoteKind(string currentKind, string? requestedKind)
    {
        if (string.Equals(requestedKind, "customer", StringComparison.OrdinalIgnoreCase)) return "customer";
        return string.IsNullOrWhiteSpace(currentKind) ? "lead" : currentKind;
    }

    private static void UpsertIdentity(CommercePartyDocument party, string channel, string identifier)
    {
        party.LinkedIdentities ??= [];
        if (party.LinkedIdentities.Any(x =>
                x.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase) &&
                x.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        party.LinkedIdentities.Add(new CommerceIdentityLink
        {
            Channel = channel,
            Identifier = identifier
        });
    }
}

public sealed class CommercePartyDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string Kind { get; set; } = "lead";
    public string Channel { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? LastSessionId { get; set; }
    public string? LastThreadId { get; set; }
    public List<CommerceIdentityLink> LinkedIdentities { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CommerceIdentityLink
{
    public string Channel { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
}

public sealed class CommerceSaleDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string PartyId { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public decimal Total { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public string State { get; set; } = "sale_created";
    public string PaymentMethod { get; set; } = "cash";
    public string? SessionId { get; set; }
    public string? ThreadId { get; set; }
    public List<CommerceLineItem> Items { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CommerceOrderDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string PartyId { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public decimal Total { get; set; }
    public string Status { get; set; } = "draft";
    public string? SessionId { get; set; }
    public string? ThreadId { get; set; }
    public List<CommerceLineItem> Items { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CommerceInvoiceDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string PartyId { get; set; } = string.Empty;
    public string? SaleId { get; set; }
    public string? OrderId { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal Total { get; set; }
    public string Status { get; set; } = "issued";
    public string Number { get; set; } = string.Empty;
    public DateTimeOffset? IssuedAt { get; set; }
    public string? SessionId { get; set; }
    public string? ThreadId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CommerceInventoryItemDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int OnHand { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class CommerceLineItem
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal Subtotal => UnitPrice * Quantity;
}

public sealed class CommerceInventoryMovementDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Delta { get; set; }
    public int Balance { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record SaleCalculationResult(decimal Subtotal, decimal Discount, decimal Tax, decimal Total);
