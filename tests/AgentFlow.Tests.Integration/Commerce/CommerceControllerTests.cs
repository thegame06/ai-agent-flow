using AgentFlow.Api.Commerce;
using AgentFlow.Api.Controllers;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Security;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AgentFlow.Tests.Integration.Commerce;

public sealed class CommerceControllerTests
{
    private const string TenantId = "tenant-1";

    [Fact]
    public async Task ResolveParty_LeadThenCustomer_LinksIdentitiesByPhone()
    {
        var sessionRepo = new StubChannelSessionRepository(ChannelSession.Create(TenantId, "ch-wa", ChannelType.WhatsApp, "+50588887777"));
        var store = new InMemoryCommerceStore();
        var controller = BuildController(sessionRepo, store);

        var lead = await controller.ResolveParty(TenantId, new ResolvePartyRequest
        {
            Channel = "WhatsApp",
            Identifier = "+50588887777",
            DisplayName = "Juan",
            Kind = "lead",
            Phone = "+505 8888 7777"
        }, CancellationToken.None);

        var leadOk = Assert.IsType<OkObjectResult>(lead);
        var leadPayload = ToJson(leadOk.Value);
        var partyId = leadPayload.GetProperty("id").GetString()!;

        var customer = await controller.ResolveParty(TenantId, new ResolvePartyRequest
        {
            Channel = "WebChat",
            Identifier = "user-abc",
            FullName = "Juan Perez",
            Email = "juan@test.local",
            Kind = "customer",
            Phone = "+50588887777"
        }, CancellationToken.None);

        var customerOk = Assert.IsType<OkObjectResult>(customer);
        var customerPayload = ToJson(customerOk.Value);
        Assert.Equal(partyId, customerPayload.GetProperty("id").GetString());
        Assert.Equal("customer", customerPayload.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task CommerceFlow_CreateSaleOrderInvoice_WithActiveSession_Works()
    {
        var session = ChannelSession.Create(TenantId, "ch-wa", ChannelType.WhatsApp, "+50581112233");
        var sessionRepo = new StubChannelSessionRepository(session);
        var store = new InMemoryCommerceStore();
        store.SeedInventory(TenantId, "SKU-1", "Producto A", 10, 20);
        store.SeedInventory(TenantId, "SKU-2", "Producto B", 15, 20);
        var controller = BuildController(sessionRepo, store);

        var partyResult = await controller.ResolveParty(TenantId, new ResolvePartyRequest
        {
            Channel = "WhatsApp",
            Identifier = "+50581112233",
            Kind = "customer",
            Phone = "+50581112233",
            SessionId = session.Id
        }, CancellationToken.None);
        var partyOk = Assert.IsType<OkObjectResult>(partyResult);
        var party = ToJson(partyOk.Value);
        var partyId = party.GetProperty("id").GetString()!;

        var saleResult = await controller.CreateSale(TenantId, new CreateSaleRequest
        {
            PartyId = partyId,
            SessionId = session.Id,
            Items =
            [
                new CommerceLineItemRequest { Sku = "SKU-1", Name = "Producto A", UnitPrice = 10, Quantity = 2 }
            ]
        }, CancellationToken.None);
        var saleOk = Assert.IsType<OkObjectResult>(saleResult);
        var sale = ToJson(saleOk.Value);
        Assert.Equal(20m, sale.GetProperty("total").GetDecimal());

        var orderResult = await controller.CreateOrder(TenantId, new CreateOrderRequest
        {
            PartyId = partyId,
            SessionId = session.Id,
            Items =
            [
                new CommerceLineItemRequest { Sku = "SKU-2", Name = "Producto B", UnitPrice = 15, Quantity = 1 }
            ]
        }, CancellationToken.None);
        var orderOk = Assert.IsType<OkObjectResult>(orderResult);
        var order = ToJson(orderOk.Value);
        Assert.Equal(15m, order.GetProperty("total").GetDecimal());

        var invoiceResult = await controller.CreateInvoice(TenantId, new CreateInvoiceRequest
        {
            PartyId = partyId,
            SessionId = session.Id,
            SaleId = sale.GetProperty("id").GetString(),
            OrderId = order.GetProperty("id").GetString(),
            Total = 35
        }, CancellationToken.None);
        var invoiceOk = Assert.IsType<OkObjectResult>(invoiceResult);
        var invoice = ToJson(invoiceOk.Value);
        Assert.Equal(35m, invoice.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task SearchInventory_ReturnsProducts()
    {
        var sessionRepo = new StubChannelSessionRepository(null);
        var store = new InMemoryCommerceStore();
        store.SeedInventory(TenantId, "SKU-TEST", "Producto Test", 99, 4);
        var controller = BuildController(sessionRepo, store);

        var result = await controller.SearchInventory(TenantId, "TEST", 20, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value);
        Assert.Single(payload);
    }

    [Fact]
    public async Task UpsertInventory_SavesItemTypeUnitAndTracking()
    {
        var sessionRepo = new StubChannelSessionRepository(null);
        var store = new InMemoryCommerceStore();
        var controller = BuildController(sessionRepo, store);

        var result = await controller.UpsertInventoryItem(TenantId, "SRV-HORA", new UpsertInventoryRequest
        {
            Name = "Consultoria por hora",
            ItemType = "service",
            UnitOfMeasure = "hour",
            TracksInventory = false,
            UnitPrice = 25,
            OnHand = 99,
            Active = true
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = ToJson(ok.Value);
        Assert.Equal("service", payload.GetProperty("itemType").GetString());
        Assert.Equal("hour", payload.GetProperty("unitOfMeasure").GetString());
        Assert.False(payload.GetProperty("tracksInventory").GetBoolean());
        Assert.Equal(0, payload.GetProperty("onHand").GetInt32());
    }

    [Fact]
    public async Task CreateSale_ForService_DoesNotRequireStock()
    {
        var session = ChannelSession.Create(TenantId, "ch-wa", ChannelType.WhatsApp, "+50589991111");
        var sessionRepo = new StubChannelSessionRepository(session);
        var store = new InMemoryCommerceStore();
        store.SeedInventory(TenantId, "SRV-DAY", "Soporte diario", 40, 0, itemType: "service", unitOfMeasure: "day", tracksInventory: false);
        var controller = BuildController(sessionRepo, store);

        var partyResult = await controller.ResolveParty(TenantId, new ResolvePartyRequest
        {
            Channel = "WhatsApp",
            Identifier = "+50589991111",
            Kind = "customer",
            Phone = "+50589991111",
            SessionId = session.Id
        }, CancellationToken.None);
        var partyId = ToJson(Assert.IsType<OkObjectResult>(partyResult).Value).GetProperty("id").GetString()!;

        var saleResult = await controller.CreateSale(TenantId, new CreateSaleRequest
        {
            PartyId = partyId,
            SessionId = session.Id,
            Items =
            [
                new CommerceLineItemRequest { Sku = "SRV-DAY", Name = "Soporte diario", UnitPrice = 40, Quantity = 2 }
            ]
        }, CancellationToken.None);

        var saleOk = Assert.IsType<OkObjectResult>(saleResult);
        var sale = ToJson(saleOk.Value);
        Assert.Equal(80m, sale.GetProperty("total").GetDecimal());

        var stock = await store.GetInventoryBySkuAsync(TenantId, "SRV-DAY", CancellationToken.None);
        Assert.NotNull(stock);
        Assert.Equal(0, stock!.OnHand);
    }

    [Fact]
    public async Task UpdateSale_RecalculatesAndAdjustsInventory()
    {
        var session = ChannelSession.Create(TenantId, "ch-wa", ChannelType.WhatsApp, "+50589990000");
        var sessionRepo = new StubChannelSessionRepository(session);
        var store = new InMemoryCommerceStore();
        store.SeedInventory(TenantId, "SKU-EDIT", "Producto Editable", 12, 10);
        var controller = BuildController(sessionRepo, store);

        var partyResult = await controller.ResolveParty(TenantId, new ResolvePartyRequest
        {
            Channel = "WhatsApp",
            Identifier = "+50589990000",
            Kind = "customer",
            Phone = "+50589990000",
            SessionId = session.Id
        }, CancellationToken.None);
        var partyId = ToJson(Assert.IsType<OkObjectResult>(partyResult).Value).GetProperty("id").GetString()!;

        var saleResult = await controller.CreateSale(TenantId, new CreateSaleRequest
        {
            PartyId = partyId,
            SessionId = session.Id,
            Items =
            [
                new CommerceLineItemRequest { Sku = "SKU-EDIT", Name = "Producto Editable", UnitPrice = 12, Quantity = 2 }
            ]
        }, CancellationToken.None);
        var saleId = ToJson(Assert.IsType<OkObjectResult>(saleResult).Value).GetProperty("id").GetString()!;

        var updateResult = await controller.UpdateSale(TenantId, saleId, new UpdateSaleRequest
        {
            SessionId = session.Id,
            PaymentMethod = "card",
            State = "sale_created",
            Items =
            [
                new CommerceLineItemRequest { Sku = "SKU-EDIT", Name = "Producto Editable", UnitPrice = 12, Quantity = 3 }
            ]
        }, CancellationToken.None);

        var updateOk = Assert.IsType<OkObjectResult>(updateResult);
        var payload = ToJson(updateOk.Value);
        Assert.Equal("card", payload.GetProperty("paymentMethod").GetString());
        Assert.Equal(36m, payload.GetProperty("total").GetDecimal());

        var stock = await store.GetInventoryBySkuAsync(TenantId, "SKU-EDIT", CancellationToken.None);
        Assert.NotNull(stock);
        Assert.Equal(7, stock!.OnHand);
    }

    [Fact]
    public async Task UpdateInvoice_AndPdf_ReturnsRealPdf()
    {
        var session = ChannelSession.Create(TenantId, "ch-wa", ChannelType.WhatsApp, "+50581110000");
        var sessionRepo = new StubChannelSessionRepository(session);
        var store = new InMemoryCommerceStore();
        store.SeedInventory(TenantId, "SKU-PDF", "Producto PDF", 8, 10);
        var controller = BuildController(sessionRepo, store);

        var partyResult = await controller.ResolveParty(TenantId, new ResolvePartyRequest
        {
            Channel = "WhatsApp",
            Identifier = "+50581110000",
            Kind = "customer",
            Phone = "+50581110000",
            SessionId = session.Id
        }, CancellationToken.None);
        var partyId = ToJson(Assert.IsType<OkObjectResult>(partyResult).Value).GetProperty("id").GetString()!;

        var saleResult = await controller.CreateSale(TenantId, new CreateSaleRequest
        {
            PartyId = partyId,
            SessionId = session.Id,
            Items =
            [
                new CommerceLineItemRequest { Sku = "SKU-PDF", Name = "Producto PDF", UnitPrice = 8, Quantity = 1 }
            ]
        }, CancellationToken.None);
        var saleId = ToJson(Assert.IsType<OkObjectResult>(saleResult).Value).GetProperty("id").GetString()!;

        var invoiceResult = await controller.CreateInvoice(TenantId, new CreateInvoiceRequest
        {
            PartyId = partyId,
            SessionId = session.Id,
            SaleId = saleId,
            Total = 8
        }, CancellationToken.None);
        var invoiceId = ToJson(Assert.IsType<OkObjectResult>(invoiceResult).Value).GetProperty("id").GetString()!;

        var updateResult = await controller.UpdateInvoice(TenantId, invoiceId, new UpdateInvoiceRequest
        {
            Number = "INV-ADMIN-1",
            Total = 9,
            Currency = "USD",
            Status = "paid"
        }, CancellationToken.None);
        var updatePayload = ToJson(Assert.IsType<OkObjectResult>(updateResult).Value);
        Assert.Equal("INV-ADMIN-1", updatePayload.GetProperty("number").GetString());
        Assert.Equal("paid", updatePayload.GetProperty("status").GetString());

        var pdfResult = await controller.GetInvoicePdf(TenantId, invoiceId, CancellationToken.None);
        var file = Assert.IsType<FileContentResult>(pdfResult);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(file.FileContents));
    }

    private static CommerceController BuildController(IChannelSessionRepository sessionRepo, ICommerceStore store)
    {
        var tenantContext = new TenantContextAccessor();
        tenantContext.Set(new TenantContext
        {
            TenantId = TenantId,
            UserId = "u1",
            Permissions = [AgentFlowPermissions.ToolRead]
        });

        return new CommerceController(tenantContext, sessionRepo, store, new NoopChannelGateway(), null);
    }

    private static JsonElement ToJson(object? value)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private sealed class StubChannelSessionRepository : IChannelSessionRepository
    {
        private readonly ChannelSession? _session;
        public StubChannelSessionRepository(ChannelSession? session) => _session = session;

        public Task<ChannelSession?> GetByIdAsync(string sessionId, string tenantId, CancellationToken ct = default)
            => Task.FromResult(_session is not null && _session.Id == sessionId && _session.TenantId == tenantId ? _session : null);
        public Task<ChannelSession?> GetByThreadIdAsync(string threadId, string tenantId, CancellationToken ct = default)
            => Task.FromResult(_session is not null && _session.ThreadId == threadId && _session.TenantId == tenantId ? _session : null);
        public Task<ChannelSession?> GetByChannelAndIdentifierAsync(string channelId, string identifier, string tenantId, CancellationToken ct = default)
            => Task.FromResult<ChannelSession?>(null);
        public Task<IReadOnlyList<ChannelSession>> GetActiveByChannelAsync(string channelId, string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelSession>>(Array.Empty<ChannelSession>());
        public Task<IReadOnlyList<ChannelSession>> GetActiveByUserAsync(string userIdentifier, string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelSession>>(Array.Empty<ChannelSession>());
        public Task<(IReadOnlyList<ChannelSession> Items, long Total)> SearchAsync(string tenantId, string? channelId = null, string? status = null, string? operationalState = null, string? query = null, int page = 0, int pageSize = 25, CancellationToken ct = default)
            => Task.FromResult(((IReadOnlyList<ChannelSession>)Array.Empty<ChannelSession>(), 0L));
        public Task<AgentFlow.Abstractions.Result> InsertAsync(ChannelSession session, CancellationToken ct = default) => Task.FromResult(AgentFlow.Abstractions.Result.Success());
        public Task<AgentFlow.Abstractions.Result> UpdateAsync(ChannelSession session, CancellationToken ct = default) => Task.FromResult(AgentFlow.Abstractions.Result.Success());
        public Task<AgentFlow.Abstractions.Result> DeleteAsync(string sessionId, string tenantId, CancellationToken ct = default) => Task.FromResult(AgentFlow.Abstractions.Result.Success());
        public Task<int> GetActiveCountAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class InMemoryCommerceStore : ICommerceStore
    {
        private readonly List<CommercePartyDocument> _parties = [];
        private readonly List<CommerceSaleDocument> _sales = [];
        private readonly List<CommerceOrderDocument> _orders = [];
        private readonly List<CommerceInvoiceDocument> _invoices = [];
        private readonly List<CommerceInventoryItemDocument> _inventory = [];
        private readonly List<CommerceCategoryDocument> _categories = [];
        private readonly List<CommerceBranchDocument> _branches = [];
        private CommerceStoreSettingsDocument? _storeSettings;

        public void SeedInventory(string tenantId, string sku, string name, decimal unitPrice, int onHand, string itemType = "physical", string unitOfMeasure = "unit", bool tracksInventory = true)
        {
            _inventory.Add(new CommerceInventoryItemDocument
            {
                TenantId = tenantId,
                Sku = sku,
                Name = name,
                ItemType = itemType,
                UnitOfMeasure = unitOfMeasure,
                TracksInventory = tracksInventory,
                UnitPrice = unitPrice,
                OnHand = onHand
            });
        }

        public Task<CommercePartyDocument> UpsertPartyByChannelIdentityAsync(string tenantId, string channel, string identifier, string? displayName, string kind, string? sourceSessionId, string? sourceThreadId, string? phone, string? email, string? fullName, CancellationToken ct)
        {
            var normalizedPhone = string.IsNullOrWhiteSpace(phone) ? null : new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
            var match = _parties.FirstOrDefault(x =>
                x.TenantId == tenantId &&
                ((!string.IsNullOrWhiteSpace(normalizedPhone) && x.Phone == normalizedPhone) ||
                 (x.Channel == channel && x.Identifier == identifier)));

            if (match is null)
            {
                match = new CommercePartyDocument
                {
                    TenantId = tenantId,
                    Channel = channel,
                    Identifier = identifier,
                    Kind = kind,
                    Phone = normalizedPhone,
                    Email = email,
                    FullName = fullName,
                    DisplayName = displayName,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    LinkedIdentities =
                    [
                        new CommerceIdentityLink { Channel = channel, Identifier = identifier }
                    ]
                };
                _parties.Add(match);
                return Task.FromResult(match);
            }

            if (string.Equals(kind, "customer", StringComparison.OrdinalIgnoreCase))
                match.Kind = "customer";
            match.Phone = normalizedPhone ?? match.Phone;
            match.Email = email ?? match.Email;
            match.FullName = fullName ?? match.FullName;
            match.DisplayName = displayName ?? match.DisplayName;
            if (!match.LinkedIdentities.Any(x => x.Channel == channel && x.Identifier == identifier))
            {
                match.LinkedIdentities.Add(new CommerceIdentityLink { Channel = channel, Identifier = identifier });
            }

            return Task.FromResult(match);
        }

        public Task<CommercePartyDocument?> GetPartyByIdAsync(string tenantId, string partyId, CancellationToken ct)
            => Task.FromResult(_parties.SingleOrDefault(x => x.TenantId == tenantId && x.Id == partyId));

        public Task<CommercePartyDocument?> GetPartyByIdentityAsync(string tenantId, string channel, string identifier, CancellationToken ct)
            => Task.FromResult(_parties.SingleOrDefault(x => x.TenantId == tenantId && x.LinkedIdentities.Any(l => l.Channel == channel && l.Identifier == identifier)));

        public Task<IReadOnlyList<CommercePartyDocument>> SearchPartiesAsync(string tenantId, string? query, int page, int pageSize, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CommercePartyDocument>>(_parties.Where(x => x.TenantId == tenantId).ToList());

        public Task<long> CountPartiesAsync(string tenantId, string? query, CancellationToken ct)
            => Task.FromResult((long)_parties.Count(x => x.TenantId == tenantId));

        public Task<CommercePartyDocument?> UpdatePartyAsync(string tenantId, string partyId, string? fullName, string? email, string? phone, string? displayName, string? kind, CancellationToken ct)
            => Task.FromResult(_parties.SingleOrDefault(x => x.TenantId == tenantId && x.Id == partyId));

        public Task<bool> DeletePartyAsync(string tenantId, string partyId, CancellationToken ct)
            => Task.FromResult(_parties.RemoveAll(x => x.TenantId == tenantId && x.Id == partyId) > 0);

        public Task<IReadOnlyList<CommerceInventoryItemDocument>> SearchInventoryAsync(string tenantId, string? query, int limit, CancellationToken ct)
        {
            var rows = _inventory.Where(x => x.TenantId == tenantId &&
                                             (string.IsNullOrWhiteSpace(query) ||
                                              x.Sku.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                              x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<CommerceInventoryItemDocument>>(rows);
        }

        public Task<CommerceInventoryItemDocument?> GetInventoryBySkuAsync(string tenantId, string sku, CancellationToken ct)
            => Task.FromResult(_inventory.SingleOrDefault(x => x.TenantId == tenantId && x.Sku == sku));

        public Task<CommerceInventoryItemDocument> UpsertInventoryItemAsync(
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
            var current = _inventory.SingleOrDefault(x => x.TenantId == tenantId && x.Sku == sku);
            var normalizedType = string.IsNullOrWhiteSpace(itemType) ? "physical" : itemType.Trim().ToLowerInvariant();
            var normalizedUnit = string.IsNullOrWhiteSpace(unitOfMeasure) ? "unit" : unitOfMeasure.Trim().ToLowerInvariant();
            var resolvedTracks = tracksInventory ?? (normalizedType is "physical" or "combo" or "kit");
            if (current is null)
            {
                current = new CommerceInventoryItemDocument
                {
                    TenantId = tenantId,
                    Sku = sku,
                    Name = name,
                    ItemType = normalizedType,
                    Description = description,
                    UnitOfMeasure = normalizedUnit,
                    TracksInventory = resolvedTracks,
                    UnitPrice = unitPrice,
                    OnHand = resolvedTracks ? onHand : 0,
                    Active = active,
                    CategoryIds = categoryIds?.ToList(),
                    BranchIds = branchIds?.ToList(),
                    ImageUrls = imageUrls?.ToList(),
                    Attributes = attributes?.ToList(),
                    Discount = discount,
                    Variations = variations?.ToList(),
                    BranchStocks = branchStocks?.ToList()
                };
                _inventory.Add(current);
            }
            else
            {
                current.Name = name;
                current.ItemType = normalizedType;
                current.Description = description;
                current.UnitOfMeasure = normalizedUnit;
                current.TracksInventory = resolvedTracks;
                current.UnitPrice = unitPrice;
                current.OnHand = resolvedTracks ? onHand : 0;
                current.Active = active;
                current.CategoryIds = categoryIds?.ToList();
                current.BranchIds = branchIds?.ToList();
                current.ImageUrls = imageUrls?.ToList();
                current.Attributes = attributes?.ToList();
                current.Discount = discount;
                current.Variations = variations?.ToList();
                current.BranchStocks = branchStocks?.ToList();
            }
            return Task.FromResult(current);
        }

        public Task<IReadOnlyList<CommerceCategoryDocument>> SearchCategoriesAsync(string tenantId, string? query, int page, int pageSize, CancellationToken ct)
        {
            var rows = _categories.Where(x => x.TenantId == tenantId &&
                                              (string.IsNullOrWhiteSpace(query) ||
                                               x.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                               x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .Skip(Math.Max(page, 0) * pageSize)
                .Take(pageSize)
                .ToList();
            return Task.FromResult<IReadOnlyList<CommerceCategoryDocument>>(rows);
        }

        public Task<long> CountCategoriesAsync(string tenantId, string? query, CancellationToken ct)
            => Task.FromResult((long)_categories.Count(x => x.TenantId == tenantId &&
                                                            (string.IsNullOrWhiteSpace(query) ||
                                                             x.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                                             x.Name.Contains(query, StringComparison.OrdinalIgnoreCase))));

        public Task<CommerceCategoryDocument> UpsertCategoryAsync(CommerceCategoryDocument category, CancellationToken ct)
        {
            var current = _categories.SingleOrDefault(x => x.TenantId == category.TenantId && x.Id == category.Id);
            if (current is null)
            {
                _categories.Add(category);
            }
            else
            {
                _categories[_categories.IndexOf(current)] = category;
            }

            return Task.FromResult(category);
        }

        public Task<CommerceCategoryDocument?> GetCategoryByIdAsync(string tenantId, string categoryId, CancellationToken ct)
            => Task.FromResult(_categories.SingleOrDefault(x => x.TenantId == tenantId && x.Id == categoryId));

        public Task<bool> DeleteCategoryAsync(string tenantId, string categoryId, CancellationToken ct)
            => Task.FromResult(_categories.RemoveAll(x => x.TenantId == tenantId && x.Id == categoryId) > 0);

        public Task<IReadOnlyList<CommerceBranchDocument>> SearchBranchesAsync(string tenantId, string? query, int page, int pageSize, CancellationToken ct)
        {
            var rows = _branches.Where(x => x.TenantId == tenantId &&
                                            (string.IsNullOrWhiteSpace(query) ||
                                             x.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                             x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .Skip(Math.Max(page, 0) * pageSize)
                .Take(pageSize)
                .ToList();
            return Task.FromResult<IReadOnlyList<CommerceBranchDocument>>(rows);
        }

        public Task<long> CountBranchesAsync(string tenantId, string? query, CancellationToken ct)
            => Task.FromResult((long)_branches.Count(x => x.TenantId == tenantId &&
                                                          (string.IsNullOrWhiteSpace(query) ||
                                                           x.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                                           x.Name.Contains(query, StringComparison.OrdinalIgnoreCase))));

        public Task<CommerceBranchDocument> UpsertBranchAsync(CommerceBranchDocument branch, CancellationToken ct)
        {
            var current = _branches.SingleOrDefault(x => x.TenantId == branch.TenantId && x.Id == branch.Id);
            if (current is null)
            {
                _branches.Add(branch);
            }
            else
            {
                _branches[_branches.IndexOf(current)] = branch;
            }

            return Task.FromResult(branch);
        }

        public Task<CommerceBranchDocument?> GetBranchByIdAsync(string tenantId, string branchId, CancellationToken ct)
            => Task.FromResult(_branches.SingleOrDefault(x => x.TenantId == tenantId && x.Id == branchId));

        public Task<bool> DeleteBranchAsync(string tenantId, string branchId, CancellationToken ct)
            => Task.FromResult(_branches.RemoveAll(x => x.TenantId == tenantId && x.Id == branchId) > 0);

        public Task<CommerceInventoryItemDocument> AdjustInventoryAsync(string tenantId, string sku, int delta, string reason, string? referenceId, CancellationToken ct)
        {
            var current = _inventory.Single(x => x.TenantId == tenantId && x.Sku == sku);
            if (current.TracksInventory)
                current.OnHand += delta;
            return Task.FromResult(current);
        }

        public Task<IReadOnlyList<CommerceInventoryMovementDocument>> SearchInventoryMovementsAsync(string tenantId, string? sku, int page, int pageSize, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CommerceInventoryMovementDocument>>(Array.Empty<CommerceInventoryMovementDocument>());

        public Task<long> CountInventoryMovementsAsync(string tenantId, string? sku, CancellationToken ct)
            => Task.FromResult(0L);

        public Task<CommerceSaleDocument> CreateSaleAsync(CommerceSaleDocument sale, CancellationToken ct)
        {
            sale.CreatedAt = DateTimeOffset.UtcNow;
            _sales.Add(sale);
            return Task.FromResult(sale);
        }

        public Task<CommerceSaleDocument?> GetSaleByIdAsync(string tenantId, string saleId, CancellationToken ct)
            => Task.FromResult(_sales.SingleOrDefault(x => x.TenantId == tenantId && x.Id == saleId));

        public Task<IReadOnlyList<CommerceSaleDocument>> SearchSalesAsync(string tenantId, string? partyId, string? state, int page, int pageSize, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CommerceSaleDocument>>(_sales.Where(x => x.TenantId == tenantId).ToList());

        public Task<long> CountSalesAsync(string tenantId, string? partyId, string? state, CancellationToken ct)
            => Task.FromResult((long)_sales.Count(x => x.TenantId == tenantId));

        public Task<CommerceSaleDocument> UpdateSaleAsync(CommerceSaleDocument sale, CancellationToken ct) => Task.FromResult(sale);

        public Task<SaleCalculationResult> CalculateSaleAsync(string tenantId, IReadOnlyList<CommerceLineItem> items, decimal? discountAmount, decimal? discountPercent, bool applyTax, decimal taxRate, CancellationToken ct)
        {
            var subtotal = items.Sum(x => x.Subtotal);
            return Task.FromResult(new SaleCalculationResult(subtotal, 0m, 0m, subtotal));
        }

        public Task<CommerceOrderDocument> CreateOrderAsync(CommerceOrderDocument order, CancellationToken ct)
        {
            order.CreatedAt = DateTimeOffset.UtcNow;
            _orders.Add(order);
            return Task.FromResult(order);
        }

        public Task<CommerceOrderDocument?> GetOrderByIdAsync(string tenantId, string orderId, CancellationToken ct)
            => Task.FromResult(_orders.SingleOrDefault(x => x.TenantId == tenantId && x.Id == orderId));

        public Task<IReadOnlyList<CommerceOrderDocument>> SearchOrdersAsync(string tenantId, string? partyId, string? status, int page, int pageSize, CancellationToken ct)
        {
            var rows = _orders.Where(x => x.TenantId == tenantId &&
                                          (string.IsNullOrWhiteSpace(partyId) || x.PartyId == partyId) &&
                                          (string.IsNullOrWhiteSpace(status) || string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase)))
                .Skip(Math.Max(page, 0) * pageSize)
                .Take(pageSize)
                .ToList();
            return Task.FromResult<IReadOnlyList<CommerceOrderDocument>>(rows);
        }

        public Task<long> CountOrdersAsync(string tenantId, string? partyId, string? status, CancellationToken ct)
            => Task.FromResult((long)_orders.Count(x => x.TenantId == tenantId &&
                                                        (string.IsNullOrWhiteSpace(partyId) || x.PartyId == partyId) &&
                                                        (string.IsNullOrWhiteSpace(status) || string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase))));

        public Task<CommerceOrderDocument> UpdateOrderAsync(CommerceOrderDocument order, CancellationToken ct)
        {
            var current = _orders.SingleOrDefault(x => x.TenantId == order.TenantId && x.Id == order.Id);
            if (current is null)
            {
                _orders.Add(order);
            }
            else
            {
                _orders[_orders.IndexOf(current)] = order;
            }

            return Task.FromResult(order);
        }

        public Task<CommerceInvoiceDocument> CreateInvoiceAsync(CommerceInvoiceDocument invoice, CancellationToken ct)
        {
            invoice.CreatedAt = DateTimeOffset.UtcNow;
            _invoices.Add(invoice);
            return Task.FromResult(invoice);
        }

        public Task<CommerceInvoiceDocument?> GetInvoiceByIdAsync(string tenantId, string invoiceId, CancellationToken ct)
            => Task.FromResult(_invoices.SingleOrDefault(x => x.TenantId == tenantId && x.Id == invoiceId));

        public Task<IReadOnlyList<CommerceInvoiceDocument>> SearchInvoicesAsync(string tenantId, string? partyId, string? status, int page, int pageSize, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CommerceInvoiceDocument>>(_invoices.Where(x => x.TenantId == tenantId).ToList());

        public Task<long> CountInvoicesAsync(string tenantId, string? partyId, string? status, CancellationToken ct)
            => Task.FromResult((long)_invoices.Count(x => x.TenantId == tenantId));

        public Task<CommerceInvoiceDocument> UpdateInvoiceAsync(CommerceInvoiceDocument invoice, CancellationToken ct)
            => Task.FromResult(invoice);

        public Task<CommerceInvoiceDocument> UpdateInvoiceStatusAsync(string tenantId, string invoiceId, string status, CancellationToken ct)
        {
            var invoice = _invoices.Single(x => x.TenantId == tenantId && x.Id == invoiceId);
            invoice.Status = status;
            return Task.FromResult(invoice);
        }

        public Task<CommerceStoreSettingsDocument> GetStoreSettingsAsync(string tenantId, CancellationToken ct)
        {
            if (_storeSettings is null || _storeSettings.TenantId != tenantId)
            {
                _storeSettings = new CommerceStoreSettingsDocument
                {
                    TenantId = tenantId
                };
            }

            return Task.FromResult(_storeSettings);
        }

        public Task<CommerceStoreSettingsDocument> UpsertStoreSettingsAsync(CommerceStoreSettingsDocument settings, CancellationToken ct)
        {
            _storeSettings = settings;
            return Task.FromResult(settings);
        }
    }

    private sealed class NoopChannelGateway : AgentFlow.Application.Channels.IChannelGateway
    {
        public void RegisterHandler(AgentFlow.Application.Channels.IChannelHandler handler) { }
        public AgentFlow.Application.Channels.IChannelHandler? GetHandler(ChannelType channelType) => null;
        public Task<ChannelMessage> ProcessMessageAsync(ChannelMessage incomingMessage, CancellationToken ct = default) => Task.FromResult(incomingMessage);
        public Task<AgentFlow.Application.Channels.SendResult> SendMessageAsync(string channelId, ChannelMessage message, CancellationToken ct = default) => Task.FromResult(AgentFlow.Application.Channels.SendResult.Ok("m1"));
        public Task<IReadOnlyList<ChannelSession>> GetActiveSessionsAsync(string channelId, string tenantId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ChannelSession>>(Array.Empty<ChannelSession>());
        public Task CloseSessionAsync(string sessionId, string tenantId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<AgentFlow.Application.Channels.BroadcastResult> BroadcastAsync(string channelId, string tenantId, string content, CancellationToken ct = default) => Task.FromResult(AgentFlow.Application.Channels.BroadcastResult.Ok(0));
    }
}
