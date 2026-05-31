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
    Task<CommerceInventoryItemDocument> UpsertInventoryItemAsync(
        string tenantId,
        string sku,
        string name,
        decimal unitPrice,
        int onHand,
        bool active,
        string? itemType,
        string? unitOfMeasure,
        bool? tracksInventory,
        string? description,
        IReadOnlyList<string>? categoryIds,
        IReadOnlyList<string>? branchIds,
        IReadOnlyList<string>? imageUrls,
        IReadOnlyList<CommerceProductAttributeDocument>? attributes,
        CommerceProductDiscountDocument? discount,
        IReadOnlyList<CommerceProductVariationDocument>? variations,
        IReadOnlyList<CommerceBranchStockDocument>? branchStocks,
        CancellationToken ct);
    Task<CommerceInventoryItemDocument> AdjustInventoryAsync(string tenantId, string sku, int delta, string reason, string? referenceId, CancellationToken ct);
    Task<IReadOnlyList<CommerceInventoryMovementDocument>> SearchInventoryMovementsAsync(string tenantId, string? sku, int page, int pageSize, CancellationToken ct);
    Task<long> CountInventoryMovementsAsync(string tenantId, string? sku, CancellationToken ct);
    Task<IReadOnlyList<CommerceCategoryDocument>> SearchCategoriesAsync(string tenantId, string? query, int page, int pageSize, CancellationToken ct);
    Task<long> CountCategoriesAsync(string tenantId, string? query, CancellationToken ct);
    Task<CommerceCategoryDocument> UpsertCategoryAsync(CommerceCategoryDocument category, CancellationToken ct);
    Task<CommerceCategoryDocument?> GetCategoryByIdAsync(string tenantId, string categoryId, CancellationToken ct);
    Task<bool> DeleteCategoryAsync(string tenantId, string categoryId, CancellationToken ct);
    Task<IReadOnlyList<CommerceBranchDocument>> SearchBranchesAsync(string tenantId, string? query, int page, int pageSize, CancellationToken ct);
    Task<long> CountBranchesAsync(string tenantId, string? query, CancellationToken ct);
    Task<CommerceBranchDocument> UpsertBranchAsync(CommerceBranchDocument branch, CancellationToken ct);
    Task<CommerceBranchDocument?> GetBranchByIdAsync(string tenantId, string branchId, CancellationToken ct);
    Task<bool> DeleteBranchAsync(string tenantId, string branchId, CancellationToken ct);
    Task<CommerceSaleDocument> CreateSaleAsync(CommerceSaleDocument sale, CancellationToken ct);
    Task<CommerceSaleDocument?> GetSaleByIdAsync(string tenantId, string saleId, CancellationToken ct);
    Task<IReadOnlyList<CommerceSaleDocument>> SearchSalesAsync(string tenantId, string? partyId, string? state, int page, int pageSize, CancellationToken ct);
    Task<long> CountSalesAsync(string tenantId, string? partyId, string? state, CancellationToken ct);
    Task<CommerceSaleDocument> UpdateSaleAsync(CommerceSaleDocument sale, CancellationToken ct);
    Task<SaleCalculationResult> CalculateSaleAsync(string tenantId, IReadOnlyList<CommerceLineItem> items, decimal? discountAmount, decimal? discountPercent, bool applyTax, decimal taxRate, CancellationToken ct);
    Task<CommerceOrderDocument> CreateOrderAsync(CommerceOrderDocument order, CancellationToken ct);
    Task<CommerceOrderDocument?> GetOrderByIdAsync(string tenantId, string orderId, CancellationToken ct);
    Task<IReadOnlyList<CommerceOrderDocument>> SearchOrdersAsync(string tenantId, string? partyId, string? status, int page, int pageSize, CancellationToken ct);
    Task<long> CountOrdersAsync(string tenantId, string? partyId, string? status, CancellationToken ct);
    Task<CommerceOrderDocument> UpdateOrderAsync(CommerceOrderDocument order, CancellationToken ct);
    Task<CommerceInvoiceDocument> CreateInvoiceAsync(CommerceInvoiceDocument invoice, CancellationToken ct);
    Task<CommerceInvoiceDocument?> GetInvoiceByIdAsync(string tenantId, string invoiceId, CancellationToken ct);
    Task<IReadOnlyList<CommerceInvoiceDocument>> SearchInvoicesAsync(string tenantId, string? partyId, string? status, int page, int pageSize, CancellationToken ct);
    Task<long> CountInvoicesAsync(string tenantId, string? partyId, string? status, CancellationToken ct);
    Task<CommerceInvoiceDocument> UpdateInvoiceAsync(CommerceInvoiceDocument invoice, CancellationToken ct);
    Task<CommerceInvoiceDocument> UpdateInvoiceStatusAsync(string tenantId, string invoiceId, string status, CancellationToken ct);
    Task<CommerceStoreSettingsDocument> GetStoreSettingsAsync(string tenantId, CancellationToken ct);
    Task<CommerceStoreSettingsDocument> UpsertStoreSettingsAsync(CommerceStoreSettingsDocument settings, CancellationToken ct);
}

public sealed class CommerceStore : ICommerceStore
{
    private readonly IMongoCollection<CommercePartyDocument> _parties;
    private readonly IMongoCollection<CommerceSaleDocument> _sales;
    private readonly IMongoCollection<CommerceOrderDocument> _orders;
    private readonly IMongoCollection<CommerceInvoiceDocument> _invoices;
    private readonly IMongoCollection<CommerceInventoryItemDocument> _inventory;
    private readonly IMongoCollection<CommerceInventoryMovementDocument> _inventoryMovements;
    private readonly IMongoCollection<CommerceCategoryDocument> _categories;
    private readonly IMongoCollection<CommerceBranchDocument> _branches;
    private readonly IMongoCollection<CommerceStoreSettingsDocument> _storeSettings;

    public CommerceStore(IMongoDatabase database)
    {
        _parties = database.GetCollection<CommercePartyDocument>("commerce_parties");
        _sales = database.GetCollection<CommerceSaleDocument>("commerce_sales");
        _orders = database.GetCollection<CommerceOrderDocument>("commerce_orders");
        _invoices = database.GetCollection<CommerceInvoiceDocument>("commerce_invoices");
        _inventory = database.GetCollection<CommerceInventoryItemDocument>("commerce_inventory_items");
        _inventoryMovements = database.GetCollection<CommerceInventoryMovementDocument>("commerce_inventory_movements");
        _categories = database.GetCollection<CommerceCategoryDocument>("commerce_categories");
        _branches = database.GetCollection<CommerceBranchDocument>("commerce_branches");
        _storeSettings = database.GetCollection<CommerceStoreSettingsDocument>("commerce_store_settings");
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
            var categoryIds = await _categories.Find(
                    Builders<CommerceCategoryDocument>.Filter.And(
                        Builders<CommerceCategoryDocument>.Filter.Eq(x => x.TenantId, tenantId),
                        Builders<CommerceCategoryDocument>.Filter.Or(
                            Builders<CommerceCategoryDocument>.Filter.Regex(x => x.Name, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                            Builders<CommerceCategoryDocument>.Filter.Regex(x => x.Description, new MongoDB.Bson.BsonRegularExpression(q, "i")))))
                .Project(x => x.Id)
                .ToListAsync(ct);

            filter = Builders<CommerceInventoryItemDocument>.Filter.And(
                filter,
                Builders<CommerceInventoryItemDocument>.Filter.Or(
                    Builders<CommerceInventoryItemDocument>.Filter.Regex(x => x.Sku, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                    Builders<CommerceInventoryItemDocument>.Filter.Regex(x => x.Name, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                    Builders<CommerceInventoryItemDocument>.Filter.Regex(x => x.Description, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                    Builders<CommerceInventoryItemDocument>.Filter.ElemMatch(
                        x => x.Attributes,
                        Builders<CommerceProductAttributeDocument>.Filter.Or(
                            Builders<CommerceProductAttributeDocument>.Filter.Regex(x => x.Key, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                            Builders<CommerceProductAttributeDocument>.Filter.Regex(x => x.Value, new MongoDB.Bson.BsonRegularExpression(q, "i")))),
                    Builders<CommerceInventoryItemDocument>.Filter.ElemMatch(
                        x => x.Variations,
                        Builders<CommerceProductVariationDocument>.Filter.Or(
                            Builders<CommerceProductVariationDocument>.Filter.Regex(x => x.Sku, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                            Builders<CommerceProductVariationDocument>.Filter.Regex(x => x.Name, new MongoDB.Bson.BsonRegularExpression(q, "i")))),
                    categoryIds.Count == 0
                        ? Builders<CommerceInventoryItemDocument>.Filter.Where(_ => false)
                        : Builders<CommerceInventoryItemDocument>.Filter.AnyIn(x => x.CategoryIds, categoryIds)));
        }

        return await _inventory.Find(filter)
            .SortByDescending(x => x.Active)
            .ThenBy(x => x.Name)
            .Limit(bounded)
            .ToListAsync(ct);
    }

    public async Task<CommerceInventoryItemDocument> UpsertInventoryItemAsync(
        string tenantId,
        string sku,
        string name,
        decimal unitPrice,
        int onHand,
        bool active,
        string? itemType,
        string? unitOfMeasure,
        bool? tracksInventory,
        string? description,
        IReadOnlyList<string>? categoryIds,
        IReadOnlyList<string>? branchIds,
        IReadOnlyList<string>? imageUrls,
        IReadOnlyList<CommerceProductAttributeDocument>? attributes,
        CommerceProductDiscountDocument? discount,
        IReadOnlyList<CommerceProductVariationDocument>? variations,
        IReadOnlyList<CommerceBranchStockDocument>? branchStocks,
        CancellationToken ct)
    {
        var normalizedType = NormalizeInventoryItemType(itemType);
        var normalizedUnit = NormalizeUnitOfMeasure(unitOfMeasure);
        var resolvedTracksInventory = ResolveTracksInventory(normalizedType, tracksInventory);
        var normalizedCategories = NormalizeIds(categoryIds);
        var normalizedBranches = NormalizeIds(branchIds);
        var normalizedImages = NormalizeImageUrls(imageUrls);
        var normalizedAttributes = NormalizeAttributes(attributes);
        var normalizedDiscount = NormalizeDiscount(discount);
        var normalizedBranchStocks = NormalizeBranchStocks(branchStocks, normalizedBranches, resolvedTracksInventory);
        var normalizedVariations = NormalizeVariations(variations, normalizedBranches, resolvedTracksInventory);
        var resolvedOnHand = resolvedTracksInventory
            ? (normalizedBranchStocks.Count > 0 ? normalizedBranchStocks.Sum(x => x.OnHand) : onHand)
            : 0;
        var current = await _inventory.Find(x => x.TenantId == tenantId && x.Sku == sku).FirstOrDefaultAsync(ct);
        if (current is null)
        {
            var created = new CommerceInventoryItemDocument
            {
                TenantId = tenantId,
                Sku = sku,
                Name = name,
                UnitPrice = unitPrice,
                OnHand = resolvedOnHand,
                Active = active,
                ItemType = normalizedType,
                UnitOfMeasure = normalizedUnit,
                TracksInventory = resolvedTracksInventory,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                CategoryIds = normalizedCategories,
                BranchIds = normalizedBranches,
                ImageUrls = normalizedImages,
                Attributes = normalizedAttributes,
                Discount = normalizedDiscount,
                Variations = normalizedVariations,
                BranchStocks = normalizedBranchStocks
            };
            await _inventory.InsertOneAsync(created, cancellationToken: ct);
            return created;
        }

        current.Name = name;
        current.UnitPrice = unitPrice;
        current.OnHand = resolvedOnHand;
        current.Active = active;
        current.ItemType = normalizedType;
        current.UnitOfMeasure = normalizedUnit;
        current.TracksInventory = resolvedTracksInventory;
        current.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        current.CategoryIds = normalizedCategories;
        current.BranchIds = normalizedBranches;
        current.ImageUrls = normalizedImages;
        current.Attributes = normalizedAttributes;
        current.Discount = normalizedDiscount;
        current.Variations = normalizedVariations;
        current.BranchStocks = normalizedBranchStocks;
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

    public async Task<IReadOnlyList<CommerceCategoryDocument>> SearchCategoriesAsync(string tenantId, string? query, int page, int pageSize, CancellationToken ct)
    {
        var filter = BuildCategorySearchFilter(tenantId, query);
        return await _categories.Find(filter)
            .SortBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Skip(Math.Max(0, page) * Math.Clamp(pageSize, 1, 100))
            .Limit(Math.Clamp(pageSize, 1, 100))
            .ToListAsync(ct);
    }

    public async Task<long> CountCategoriesAsync(string tenantId, string? query, CancellationToken ct)
    {
        var filter = BuildCategorySearchFilter(tenantId, query);
        return await _categories.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task<CommerceCategoryDocument> UpsertCategoryAsync(CommerceCategoryDocument category, CancellationToken ct)
    {
        category.Name = category.Name.Trim();
        category.Description = string.IsNullOrWhiteSpace(category.Description) ? null : category.Description.Trim();
        category.ParentCategoryId = string.IsNullOrWhiteSpace(category.ParentCategoryId) ? null : category.ParentCategoryId.Trim();
        category.UpdatedAt = DateTimeOffset.UtcNow;

        var existing = await _categories.Find(x => x.TenantId == category.TenantId && x.Id == category.Id).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            category.CreatedAt = category.UpdatedAt;
            await _categories.InsertOneAsync(category, cancellationToken: ct);
            return category;
        }

        category.CreatedAt = existing.CreatedAt;
        await _categories.ReplaceOneAsync(x => x.TenantId == category.TenantId && x.Id == category.Id, category, cancellationToken: ct);
        return category;
    }

    public async Task<CommerceCategoryDocument?> GetCategoryByIdAsync(string tenantId, string categoryId, CancellationToken ct)
        => await _categories.Find(x => x.TenantId == tenantId && x.Id == categoryId).FirstOrDefaultAsync(ct);

    public async Task<bool> DeleteCategoryAsync(string tenantId, string categoryId, CancellationToken ct)
    {
        var result = await _categories.DeleteOneAsync(x => x.TenantId == tenantId && x.Id == categoryId, ct);
        if (result.DeletedCount > 0)
        {
            var categoryPull = Builders<CommerceInventoryItemDocument>.Update.Pull(x => x.CategoryIds, categoryId);
            await _inventory.UpdateManyAsync(x => x.TenantId == tenantId && x.CategoryIds.Contains(categoryId), categoryPull, cancellationToken: ct);
        }

        return result.DeletedCount > 0;
    }

    public async Task<IReadOnlyList<CommerceBranchDocument>> SearchBranchesAsync(string tenantId, string? query, int page, int pageSize, CancellationToken ct)
    {
        var filter = BuildBranchSearchFilter(tenantId, query);
        return await _branches.Find(filter)
            .SortBy(x => x.Code)
            .ThenBy(x => x.Name)
            .Skip(Math.Max(0, page) * Math.Clamp(pageSize, 1, 100))
            .Limit(Math.Clamp(pageSize, 1, 100))
            .ToListAsync(ct);
    }

    public async Task<long> CountBranchesAsync(string tenantId, string? query, CancellationToken ct)
    {
        var filter = BuildBranchSearchFilter(tenantId, query);
        return await _branches.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task<CommerceBranchDocument> UpsertBranchAsync(CommerceBranchDocument branch, CancellationToken ct)
    {
        branch.Code = branch.Code.Trim();
        branch.Name = branch.Name.Trim();
        branch.Address = string.IsNullOrWhiteSpace(branch.Address) ? null : branch.Address.Trim();
        branch.Phone = string.IsNullOrWhiteSpace(branch.Phone) ? null : NormalizePhone(branch.Phone);
        branch.Properties = NormalizeProperties(branch.Properties);
        branch.UpdatedAt = DateTimeOffset.UtcNow;

        var existing = await _branches.Find(x => x.TenantId == branch.TenantId && x.Id == branch.Id).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            branch.CreatedAt = branch.UpdatedAt;
            await _branches.InsertOneAsync(branch, cancellationToken: ct);
            return branch;
        }

        branch.CreatedAt = existing.CreatedAt;
        await _branches.ReplaceOneAsync(x => x.TenantId == branch.TenantId && x.Id == branch.Id, branch, cancellationToken: ct);
        return branch;
    }

    public async Task<CommerceBranchDocument?> GetBranchByIdAsync(string tenantId, string branchId, CancellationToken ct)
        => await _branches.Find(x => x.TenantId == tenantId && x.Id == branchId).FirstOrDefaultAsync(ct);

    public async Task<bool> DeleteBranchAsync(string tenantId, string branchId, CancellationToken ct)
    {
        var result = await _branches.DeleteOneAsync(x => x.TenantId == tenantId && x.Id == branchId, ct);
        if (result.DeletedCount > 0)
        {
            var branchPull = Builders<CommerceInventoryItemDocument>.Update.Pull(x => x.BranchIds, branchId);
            await _inventory.UpdateManyAsync(x => x.TenantId == tenantId && x.BranchIds.Contains(branchId), branchPull, cancellationToken: ct);
        }

        return result.DeletedCount > 0;
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

    public async Task<CommerceOrderDocument?> GetOrderByIdAsync(string tenantId, string orderId, CancellationToken ct)
        => await _orders.Find(x => x.TenantId == tenantId && x.Id == orderId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CommerceOrderDocument>> SearchOrdersAsync(string tenantId, string? partyId, string? status, int page, int pageSize, CancellationToken ct)
    {
        var filter = BuildOrderSearchFilter(tenantId, partyId, status);
        return await _orders.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Skip(Math.Max(0, page) * Math.Clamp(pageSize, 1, 100))
            .Limit(Math.Clamp(pageSize, 1, 100))
            .ToListAsync(ct);
    }

    public async Task<long> CountOrdersAsync(string tenantId, string? partyId, string? status, CancellationToken ct)
    {
        var filter = BuildOrderSearchFilter(tenantId, partyId, status);
        return await _orders.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task<CommerceOrderDocument> UpdateOrderAsync(CommerceOrderDocument order, CancellationToken ct)
    {
        await _orders.ReplaceOneAsync(x => x.TenantId == order.TenantId && x.Id == order.Id, order, cancellationToken: ct);
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

    public async Task<CommerceStoreSettingsDocument> GetStoreSettingsAsync(string tenantId, CancellationToken ct)
    {
        var current = await _storeSettings.Find(x => x.TenantId == tenantId).FirstOrDefaultAsync(ct);
        if (current is not null) return current;

        var created = new CommerceStoreSettingsDocument
        {
            TenantId = tenantId,
            StoreName = "Ventas y cobros",
            StoreId = $"store-{tenantId[..Math.Min(8, tenantId.Length)]}",
            ApiToken = Guid.NewGuid().ToString("N"),
            Currency = "USD",
            Language = "es",
            TaxRate = 0m,
            UsePerProductTax = false,
            HideOutOfStockProducts = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _storeSettings.InsertOneAsync(created, cancellationToken: ct);
        return created;
    }

    public async Task<CommerceStoreSettingsDocument> UpsertStoreSettingsAsync(CommerceStoreSettingsDocument settings, CancellationToken ct)
    {
        var current = await _storeSettings.Find(x => x.TenantId == settings.TenantId).FirstOrDefaultAsync(ct);
        settings.StoreName = string.IsNullOrWhiteSpace(settings.StoreName) ? "Ventas y cobros" : settings.StoreName.Trim();
        settings.StoreId = string.IsNullOrWhiteSpace(settings.StoreId) ? $"store-{settings.TenantId[..Math.Min(8, settings.TenantId.Length)]}" : settings.StoreId.Trim();
        settings.ApiToken = string.IsNullOrWhiteSpace(settings.ApiToken) ? Guid.NewGuid().ToString("N") : settings.ApiToken.Trim();
        settings.Currency = string.IsNullOrWhiteSpace(settings.Currency) ? "USD" : settings.Currency.Trim().ToUpperInvariant();
        settings.Language = string.IsNullOrWhiteSpace(settings.Language) ? "es" : settings.Language.Trim().ToLowerInvariant();
        settings.UpdatedAt = DateTimeOffset.UtcNow;

        if (current is null)
        {
            settings.CreatedAt = settings.UpdatedAt;
            await _storeSettings.InsertOneAsync(settings, cancellationToken: ct);
            return settings;
        }

        settings.Id = current.Id;
        settings.CreatedAt = current.CreatedAt;
        await _storeSettings.ReplaceOneAsync(x => x.TenantId == settings.TenantId, settings, cancellationToken: ct);
        return settings;
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

    private static FilterDefinition<CommerceOrderDocument> BuildOrderSearchFilter(string tenantId, string? partyId, string? status)
    {
        var filter = Builders<CommerceOrderDocument>.Filter.Eq(x => x.TenantId, tenantId);
        if (!string.IsNullOrWhiteSpace(partyId))
            filter = Builders<CommerceOrderDocument>.Filter.And(filter, Builders<CommerceOrderDocument>.Filter.Eq(x => x.PartyId, partyId));
        if (!string.IsNullOrWhiteSpace(status))
            filter = Builders<CommerceOrderDocument>.Filter.And(filter, Builders<CommerceOrderDocument>.Filter.Eq(x => x.Status, status));
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

    private static FilterDefinition<CommerceCategoryDocument> BuildCategorySearchFilter(string tenantId, string? query)
    {
        var filter = Builders<CommerceCategoryDocument>.Filter.Eq(x => x.TenantId, tenantId);
        if (string.IsNullOrWhiteSpace(query)) return filter;
        var q = query.Trim();
        return Builders<CommerceCategoryDocument>.Filter.And(
            filter,
            Builders<CommerceCategoryDocument>.Filter.Or(
                Builders<CommerceCategoryDocument>.Filter.Regex(x => x.Name, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                Builders<CommerceCategoryDocument>.Filter.Regex(x => x.Description, new MongoDB.Bson.BsonRegularExpression(q, "i"))));
    }

    private static FilterDefinition<CommerceBranchDocument> BuildBranchSearchFilter(string tenantId, string? query)
    {
        var filter = Builders<CommerceBranchDocument>.Filter.Eq(x => x.TenantId, tenantId);
        if (string.IsNullOrWhiteSpace(query)) return filter;
        var q = query.Trim();
        return Builders<CommerceBranchDocument>.Filter.And(
            filter,
            Builders<CommerceBranchDocument>.Filter.Or(
                Builders<CommerceBranchDocument>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                Builders<CommerceBranchDocument>.Filter.Regex(x => x.Name, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                Builders<CommerceBranchDocument>.Filter.Regex(x => x.Address, new MongoDB.Bson.BsonRegularExpression(q, "i"))));
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

    private static string NormalizeInventoryItemType(string? itemType)
    {
        var candidate = itemType?.Trim().ToLowerInvariant();
        return candidate switch
        {
            "physical" or "intangible" or "service" or "combo" or "kit" => candidate,
            _ => "physical"
        };
    }

    private static string NormalizeUnitOfMeasure(string? unitOfMeasure)
    {
        var candidate = unitOfMeasure?.Trim().ToLowerInvariant();
        return candidate switch
        {
            "unit" or "hour" or "day" or "week" or "month" or "minute" or
            "kg" or "g" or "lb" or "liter" or "ml" or "meter" or "cm" or
            "box" or "pack" or "set" => candidate,
            _ => "unit"
        };
    }

    private static bool ResolveTracksInventory(string itemType, bool? tracksInventory)
    {
        if (tracksInventory.HasValue)
            return tracksInventory.Value;

        return itemType is "physical" or "combo" or "kit";
    }

    private static List<string> NormalizeIds(IReadOnlyList<string>? ids)
    {
        if (ids is null || ids.Count == 0) return [];
        return ids
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> NormalizeImageUrls(IReadOnlyList<string>? imageUrls)
    {
        if (imageUrls is null || imageUrls.Count == 0) return [];
        return imageUrls
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> NormalizeProperties(Dictionary<string, string>? properties)
    {
        if (properties is null || properties.Count == 0) return [];
        return properties
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
            .ToDictionary(x => x.Key.Trim(), x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static List<CommerceProductAttributeDocument> NormalizeAttributes(IReadOnlyList<CommerceProductAttributeDocument>? attributes)
    {
        if (attributes is null || attributes.Count == 0) return [];
        return attributes
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => new CommerceProductAttributeDocument
            {
                Key = x.Key.Trim(),
                Value = x.Value.Trim()
            })
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static CommerceProductDiscountDocument? NormalizeDiscount(CommerceProductDiscountDocument? discount)
    {
        if (discount is null || !discount.Enabled) return null;
        var discountType = discount.Type?.Trim().ToLowerInvariant();
        if (discountType is not ("percent" or "amount")) return null;
        var value = Math.Max(0m, discount.Value);
        if (value <= 0m) return null;
        return new CommerceProductDiscountDocument
        {
            Enabled = true,
            Type = discountType,
            Value = value
        };
    }

    private static List<CommerceProductVariationDocument> NormalizeVariations(
        IReadOnlyList<CommerceProductVariationDocument>? variations,
        IReadOnlyList<string> branchIds,
        bool tracksInventory)
    {
        if (variations is null || variations.Count == 0) return [];
        return variations
            .Where(x => !string.IsNullOrWhiteSpace(x.Sku))
            .Select(x => new CommerceProductVariationDocument
            {
                Id = string.IsNullOrWhiteSpace(x.Id) ? Guid.NewGuid().ToString("N") : x.Id.Trim(),
                Sku = x.Sku.Trim(),
                Name = string.IsNullOrWhiteSpace(x.Name) ? x.Sku.Trim() : x.Name.Trim(),
                Price = Math.Max(0m, x.Price),
                Stock = Math.Max(0, x.Stock),
                Active = x.Active,
                Attributes = NormalizeAttributes(x.Attributes),
                ImageUrls = NormalizeImageUrls(x.ImageUrls),
                BranchStocks = NormalizeBranchStocks(x.BranchStocks, branchIds, tracksInventory)
            })
            .Select(x =>
            {
                x.Stock = x.BranchStocks.Count > 0
                    ? x.BranchStocks.Sum(stock => stock.OnHand)
                    : Math.Max(0, x.Stock);
                return x;
            })
            .GroupBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static List<CommerceBranchStockDocument> NormalizeBranchStocks(
        IReadOnlyList<CommerceBranchStockDocument>? branchStocks,
        IReadOnlyList<string> branchIds,
        bool tracksInventory)
    {
        if (!tracksInventory) return [];
        var normalized = branchStocks?
            .Where(x => !string.IsNullOrWhiteSpace(x.BranchId))
            .Select(x => new CommerceBranchStockDocument
            {
                BranchId = x.BranchId.Trim(),
                OnHand = Math.Max(0, x.OnHand)
            })
            .GroupBy(x => x.BranchId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList() ?? [];

        foreach (var branchId in branchIds.Where(x => !normalized.Any(y => string.Equals(y.BranchId, x, StringComparison.OrdinalIgnoreCase))))
        {
            normalized.Add(new CommerceBranchStockDocument { BranchId = branchId, OnHand = 0 });
        }

        return normalized;
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
    public string? Notes { get; set; }
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
    public string? Description { get; set; }
    public string ItemType { get; set; } = "physical";
    public string UnitOfMeasure { get; set; } = "unit";
    public bool TracksInventory { get; set; } = true;
    public decimal UnitPrice { get; set; }
    public int OnHand { get; set; }
    public bool Active { get; set; } = true;
    public List<string> CategoryIds { get; set; } = [];
    public List<string> BranchIds { get; set; } = [];
    public List<string> ImageUrls { get; set; } = [];
    public List<CommerceProductAttributeDocument> Attributes { get; set; } = [];
    public CommerceProductDiscountDocument? Discount { get; set; }
    public List<CommerceProductVariationDocument> Variations { get; set; } = [];
    public List<CommerceBranchStockDocument> BranchStocks { get; set; } = [];
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

public sealed class CommerceCategoryDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ParentCategoryId { get; set; }
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CommerceBranchDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool Active { get; set; } = true;
    public Dictionary<string, string> Properties { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CommerceStoreSettingsDocument
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string StoreName { get; set; } = "Ventas y cobros";
    public string StoreId { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public string Language { get; set; } = "es";
    public decimal TaxRate { get; set; }
    public bool UsePerProductTax { get; set; }
    public bool HideOutOfStockProducts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CommerceProductAttributeDocument
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class CommerceProductDiscountDocument
{
    public bool Enabled { get; set; }
    public string Type { get; set; } = "percent";
    public decimal Value { get; set; }
}

public sealed class CommerceProductVariationDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public bool Active { get; set; } = true;
    public List<CommerceProductAttributeDocument> Attributes { get; set; } = [];
    public List<string> ImageUrls { get; set; } = [];
    public List<CommerceBranchStockDocument> BranchStocks { get; set; } = [];
}

public sealed class CommerceBranchStockDocument
{
    public string BranchId { get; set; } = string.Empty;
    public int OnHand { get; set; }
}

public sealed record SaleCalculationResult(decimal Subtotal, decimal Discount, decimal Tax, decimal Total);
