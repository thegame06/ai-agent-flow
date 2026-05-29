using AgentFlow.Api.Commerce;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Extensions;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/commerce")]
[Authorize]
public sealed class CommerceController : ControllerBase
{
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IChannelSessionRepository _sessionRepo;
    private readonly IChannelGateway _channelGateway;
    private readonly ICommerceStore _commerce;
    private readonly IExtensionRegistry? _extensions;

    public CommerceController(
        ITenantContextAccessor tenantContext,
        IChannelSessionRepository sessionRepo,
        ICommerceStore commerce,
        IChannelGateway channelGateway,
        IExtensionRegistry? extensions = null)
    {
        _tenantContext = tenantContext;
        _sessionRepo = sessionRepo;
        _commerce = commerce;
        _channelGateway = channelGateway;
        _extensions = extensions;
    }

    [HttpPost("crm/resolve-party")]
    public async Task<IActionResult> ResolveParty(
        string tenantId,
        [FromBody] ResolvePartyRequest request,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.CommunicationInbox, ct);
        if (moduleCheck is not null) return moduleCheck;
        if (string.IsNullOrWhiteSpace(request.Channel) || string.IsNullOrWhiteSpace(request.Identifier))
            return BadRequest("channel and identifier are required.");

        string? threadId = null;
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var session = await _sessionRepo.GetByIdAsync(request.SessionId, tenantId, ct);
            if (session is null) return NotFound($"Session '{request.SessionId}' not found.");
            threadId = session.ThreadId;
        }

        var party = await _commerce.UpsertPartyByChannelIdentityAsync(
            tenantId,
            request.Channel.Trim(),
            request.Identifier.Trim(),
            request.DisplayName,
            request.Kind ?? "lead",
            request.SessionId,
            threadId,
            request.Phone,
            request.Email,
            request.FullName,
            ct);
        await SyncSessionPartyMetadataAsync(tenantId, request.SessionId, request.Channel, request.Identifier, party, ct);

        return Ok(ToPartyDto(party));
    }

    [HttpGet("crm/party-by-identity")]
    public async Task<IActionResult> GetPartyByIdentity(
        string tenantId,
        [FromQuery] string channel,
        [FromQuery] string identifier,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.CommunicationInbox, ct);
        if (moduleCheck is not null) return moduleCheck;
        var party = await _commerce.GetPartyByIdentityAsync(tenantId, channel, identifier, ct);
        if (party is null) return NotFound();
        return Ok(ToPartyDto(party));
    }

    [HttpGet("crm/customers")]
    public async Task<IActionResult> SearchCustomers(
        string tenantId,
        [FromQuery] string? query = null,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.CommunicationInbox, ct);
        if (moduleCheck is not null) return moduleCheck;
        var items = await _commerce.SearchPartiesAsync(tenantId, query, page, pageSize, ct);
        var total = await _commerce.CountPartiesAsync(tenantId, query, ct);
        return Ok(new PagedResponse<object>
        {
            Items = items.Select(ToPartyDto).ToList(),
            Total = total,
            Page = Math.Max(0, page),
            PageSize = Math.Clamp(pageSize, 1, 100)
        });
    }

    [HttpGet("crm/customers/{partyId}")]
    public async Task<IActionResult> GetCustomerById(string tenantId, string partyId, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.CommunicationInbox, ct);
        if (moduleCheck is not null) return moduleCheck;
        var party = await _commerce.GetPartyByIdAsync(tenantId, partyId, ct);
        return party is null ? NotFound() : Ok(ToPartyDto(party));
    }

    [HttpPut("crm/customers/{partyId}")]
    public async Task<IActionResult> UpdateCustomer(string tenantId, string partyId, [FromBody] UpdateCustomerRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.CommunicationInbox, ct);
        if (moduleCheck is not null) return moduleCheck;
        try
        {
            var updated = await _commerce.UpdatePartyAsync(tenantId, partyId, request.FullName, request.Email, request.Phone, request.DisplayName, request.Kind, ct);
            if (updated is null) return NotFound();
            await SyncSessionPartyMetadataAsync(tenantId, updated.LastSessionId, updated.Channel, updated.Identifier, updated, ct);
            return Ok(ToPartyDto(updated));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpDelete("crm/customers/{partyId}")]
    public async Task<IActionResult> DeleteCustomer(string tenantId, string partyId, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.CommunicationInbox, ct);
        if (moduleCheck is not null) return moduleCheck;
        var deleted = await _commerce.DeletePartyAsync(tenantId, partyId, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("conversation-context/{sessionId}")]
    public async Task<IActionResult> GetConversationContext(
        string tenantId,
        string sessionId,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.CommunicationInbox, ct);
        if (moduleCheck is not null) return moduleCheck;
        var session = await _sessionRepo.GetByIdAsync(sessionId, tenantId, ct);
        if (session is null) return NotFound();

        var party = await _commerce.GetPartyByIdentityAsync(tenantId, session.ChannelType, session.Identifier, ct);
        var commercialState = await ResolveCommercialStateAsync(tenantId, party?.Id, ct);
        return Ok(new
        {
            session.Id,
            session.ChannelType,
            session.Identifier,
            session.ThreadId,
            session.Status,
            session.ExpiresAt,
            isExpired = session.IsExpired(),
            unread = session.Metadata.TryGetValue("unread_count", out var unread) && int.TryParse(unread, out var u) ? u : 0,
            commercialState,
            party = party is null ? null : ToPartyDto(party)
        });
    }

    [HttpGet("conversation-context/by-thread/{threadId}")]
    public async Task<IActionResult> GetConversationContextByThread(
        string tenantId,
        string threadId,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.CommunicationInbox, ct);
        if (moduleCheck is not null) return moduleCheck;
        var session = await _sessionRepo.GetByThreadIdAsync(threadId, tenantId, ct);
        if (session is null) return NotFound();

        var party = await _commerce.GetPartyByIdentityAsync(tenantId, session.ChannelType, session.Identifier, ct);
        var commercialState = await ResolveCommercialStateAsync(tenantId, party?.Id, ct);
        return Ok(new
        {
            session.Id,
            session.ChannelType,
            session.Identifier,
            session.ThreadId,
            session.Status,
            session.ExpiresAt,
            isExpired = session.IsExpired(),
            unread = session.Metadata.TryGetValue("unread_count", out var unread) && int.TryParse(unread, out var u) ? u : 0,
            commercialState,
            party = party is null ? null : ToPartyDto(party)
        });
    }

    [HttpPost("conversation-context/{sessionId}/messages")]
    public async Task<IActionResult> SendConversationMessage(
        string tenantId,
        string sessionId,
        [FromBody] SendConversationMessageRequest request,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.CommunicationInbox, ct);
        if (moduleCheck is not null) return moduleCheck;
        if (string.IsNullOrWhiteSpace(request.Content)) return BadRequest("content is required.");
        var session = await _sessionRepo.GetByIdAsync(sessionId, tenantId, ct);
        if (session is null) return NotFound();
        if (session.IsExpired() || session.Status != Domain.Aggregates.SessionStatus.Active)
            return BadRequest(new { message = "Session is not active.", sessionId, session.Status, session.ExpiresAt });

        var outgoing = Domain.Aggregates.ChannelMessage.CreateOutgoing(
            tenantId,
            session.ChannelId,
            session.Id,
            session.Identifier,
            request.Content.Trim());
        outgoing.Metadata["actor"] = "agent";
        outgoing.Metadata["agentflow.delivery"] = "sent";
        var send = await _channelGateway.SendMessageAsync(session.ChannelId, outgoing, ct);
        if (!send.Success) return BadRequest(new { message = send.Error ?? "Failed to send message." });
        return Ok(new { outgoing.Id, outgoing.CreatedAt, send.MessageId });
    }

    [HttpPost("conversation-context/{sessionId}/close")]
    public async Task<IActionResult> CloseConversation(string tenantId, string sessionId, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.CommunicationInbox, ct);
        if (moduleCheck is not null) return moduleCheck;
        await _channelGateway.CloseSessionAsync(sessionId, tenantId, ct);
        return Ok(new { sessionId, closed = true });
    }

    [HttpGet("inventory/search")]
    public async Task<IActionResult> SearchInventory(
        string tenantId,
        [FromQuery] string? query = null,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Inventory, ct);
        if (moduleCheck is not null) return moduleCheck;
        var rows = await _commerce.SearchInventoryAsync(tenantId, query, limit, ct);
        return Ok(rows.Select(ToInventoryDto));
    }

    [HttpPut("inventory/items/{sku}")]
    public async Task<IActionResult> UpsertInventoryItem(
        string tenantId,
        string sku,
        [FromBody] UpsertInventoryRequest request,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Inventory, ct);
        if (moduleCheck is not null) return moduleCheck;
        var saved = await _commerce.UpsertInventoryItemAsync(
            tenantId,
            sku,
            request.Name,
            request.UnitPrice,
            request.OnHand,
            request.Active,
            request.ItemType,
            request.UnitOfMeasure,
            request.TracksInventory,
            request.Description,
            request.CategoryIds,
            request.BranchIds,
            request.ImageUrls,
            request.Attributes?.Select(x => new CommerceProductAttributeDocument { Key = x.Key, Value = x.Value }).ToList(),
            request.Discount is null ? null : new CommerceProductDiscountDocument
            {
                Enabled = request.Discount.Enabled,
                Type = request.Discount.Type,
                Value = request.Discount.Value
            },
            request.Variations?.Select(x => new CommerceProductVariationDocument
            {
                Id = x.Id ?? Guid.NewGuid().ToString("N"),
                Sku = x.Sku,
                Name = x.Name ?? x.Sku,
                Price = x.Price,
                Stock = x.Stock,
                Active = x.Active,
                Attributes = x.Attributes?.Select(a => new CommerceProductAttributeDocument { Key = a.Key, Value = a.Value }).ToList() ?? [],
                ImageUrls = x.ImageUrls ?? [],
                BranchStocks = x.BranchStocks?.Select(stock => new CommerceBranchStockDocument
                {
                    BranchId = stock.BranchId,
                    OnHand = stock.OnHand
                }).ToList() ?? []
            }).ToList(),
            request.BranchStocks?.Select(x => new CommerceBranchStockDocument
            {
                BranchId = x.BranchId,
                OnHand = x.OnHand
            }).ToList(),
            ct);
        return Ok(ToInventoryDto(saved));
    }

    [HttpPost("inventory/items/{sku}/adjust")]
    public async Task<IActionResult> AdjustInventory(
        string tenantId,
        string sku,
        [FromBody] AdjustInventoryRequest request,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Inventory, ct);
        if (moduleCheck is not null) return moduleCheck;
        try
        {
            var saved = await _commerce.AdjustInventoryAsync(tenantId, sku, request.Delta, request.Reason, request.ReferenceId, ct);
            return Ok(ToInventoryDto(saved));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("inventory/categories")]
    public async Task<IActionResult> SearchCategories(
        string tenantId,
        [FromQuery] string? query = null,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Inventory, ct);
        if (moduleCheck is not null) return moduleCheck;
        var items = await _commerce.SearchCategoriesAsync(tenantId, query, page, pageSize, ct);
        var total = await _commerce.CountCategoriesAsync(tenantId, query, ct);
        return Ok(new PagedResponse<object>
        {
            Items = items.Select(ToCategoryDto).ToList(),
            Total = total,
            Page = Math.Max(0, page),
            PageSize = Math.Clamp(pageSize, 1, 100)
        });
    }

    [HttpPost("inventory/categories")]
    public async Task<IActionResult> CreateCategory(string tenantId, [FromBody] UpsertCategoryRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Inventory, ct);
        if (moduleCheck is not null) return moduleCheck;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("name is required.");

        var saved = await _commerce.UpsertCategoryAsync(new CommerceCategoryDocument
        {
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description,
            ParentCategoryId = request.ParentCategoryId,
            SortOrder = request.SortOrder ?? 0,
            Active = request.Active
        }, ct);
        return Ok(ToCategoryDto(saved));
    }

    [HttpPut("inventory/categories/{categoryId}")]
    public async Task<IActionResult> UpdateCategory(string tenantId, string categoryId, [FromBody] UpsertCategoryRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Inventory, ct);
        if (moduleCheck is not null) return moduleCheck;
        var existing = await _commerce.GetCategoryByIdAsync(tenantId, categoryId, ct);
        if (existing is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("name is required.");

        existing.Name = request.Name;
        existing.Description = request.Description;
        existing.ParentCategoryId = request.ParentCategoryId;
        existing.SortOrder = request.SortOrder ?? existing.SortOrder;
        existing.Active = request.Active;

        var saved = await _commerce.UpsertCategoryAsync(existing, ct);
        return Ok(ToCategoryDto(saved));
    }

    [HttpDelete("inventory/categories/{categoryId}")]
    public async Task<IActionResult> DeleteCategory(string tenantId, string categoryId, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Inventory, ct);
        if (moduleCheck is not null) return moduleCheck;
        var deleted = await _commerce.DeleteCategoryAsync(tenantId, categoryId, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("inventory/branches")]
    public async Task<IActionResult> SearchBranches(
        string tenantId,
        [FromQuery] string? query = null,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Inventory, ct);
        if (moduleCheck is not null) return moduleCheck;
        var items = await _commerce.SearchBranchesAsync(tenantId, query, page, pageSize, ct);
        var total = await _commerce.CountBranchesAsync(tenantId, query, ct);
        return Ok(new PagedResponse<object>
        {
            Items = items.Select(ToBranchDto).ToList(),
            Total = total,
            Page = Math.Max(0, page),
            PageSize = Math.Clamp(pageSize, 1, 100)
        });
    }

    [HttpPost("inventory/branches")]
    public async Task<IActionResult> CreateBranch(string tenantId, [FromBody] UpsertBranchRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Inventory, ct);
        if (moduleCheck is not null) return moduleCheck;
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("code and name are required.");

        var saved = await _commerce.UpsertBranchAsync(new CommerceBranchDocument
        {
            TenantId = tenantId,
            Code = request.Code,
            Name = request.Name,
            Address = request.Address,
            Phone = request.Phone,
            Active = request.Active,
            Properties = request.Properties ?? []
        }, ct);
        return Ok(ToBranchDto(saved));
    }

    [HttpPut("inventory/branches/{branchId}")]
    public async Task<IActionResult> UpdateBranch(string tenantId, string branchId, [FromBody] UpsertBranchRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Inventory, ct);
        if (moduleCheck is not null) return moduleCheck;
        var existing = await _commerce.GetBranchByIdAsync(tenantId, branchId, ct);
        if (existing is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("code and name are required.");

        existing.Code = request.Code;
        existing.Name = request.Name;
        existing.Address = request.Address;
        existing.Phone = request.Phone;
        existing.Active = request.Active;
        existing.Properties = request.Properties ?? [];

        var saved = await _commerce.UpsertBranchAsync(existing, ct);
        return Ok(ToBranchDto(saved));
    }

    [HttpDelete("inventory/branches/{branchId}")]
    public async Task<IActionResult> DeleteBranch(string tenantId, string branchId, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Inventory, ct);
        if (moduleCheck is not null) return moduleCheck;
        var deleted = await _commerce.DeleteBranchAsync(tenantId, branchId, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("inventory/movements")]
    public async Task<IActionResult> SearchInventoryMovements(
        string tenantId,
        [FromQuery] string? sku = null,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Inventory, ct);
        if (moduleCheck is not null) return moduleCheck;
        var items = await _commerce.SearchInventoryMovementsAsync(tenantId, sku, page, pageSize, ct);
        var total = await _commerce.CountInventoryMovementsAsync(tenantId, sku, ct);
        return Ok(new PagedResponse<object>
        {
            Items = items.Select(x => new { x.Id, x.Sku, x.Delta, x.Balance, x.Reason, x.ReferenceId, x.CreatedAt }).ToList(),
            Total = total,
            Page = Math.Max(0, page),
            PageSize = Math.Clamp(pageSize, 1, 100)
        });
    }

    [HttpPost("sales")]
    public async Task<IActionResult> CreateSale(
        string tenantId,
        [FromBody] CreateSaleRequest request,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.SalesPos, ct);
        if (moduleCheck is not null) return moduleCheck;
        var activeSessionCheck = await EnsureActiveSessionIfProvided(tenantId, request.SessionId, ct);
        if (activeSessionCheck is not null)
            return activeSessionCheck;

        var party = await _commerce.GetPartyByIdAsync(tenantId, request.PartyId, ct);
        if (party is null) return NotFound($"Party '{request.PartyId}' not found.");

        foreach (var item in request.Items)
        {
            var stock = await _commerce.GetInventoryBySkuAsync(tenantId, item.Sku, ct);
            if (stock is null) return NotFound($"Inventory SKU '{item.Sku}' not found.");
            if (!stock.Active) return BadRequest($"Inventory SKU '{item.Sku}' is inactive.");
            if (stock.TracksInventory && stock.OnHand < (int)Math.Ceiling(item.Quantity))
                return Conflict(new { message = $"Insufficient stock for SKU '{item.Sku}'.", sku = item.Sku, onHand = stock.OnHand, requested = item.Quantity });
        }

        var calc = await _commerce.CalculateSaleAsync(
            tenantId,
            request.Items.Select(i => new CommerceLineItem { Sku = i.Sku, Name = i.Name, UnitPrice = i.UnitPrice, Quantity = i.Quantity }).ToList(),
            request.DiscountAmount,
            request.DiscountPercent,
            request.ApplyTax,
            request.TaxRate ?? 0.15m,
            ct);

        var doc = new CommerceSaleDocument
        {
            TenantId = tenantId,
            PartyId = request.PartyId,
            Currency = request.Currency ?? "USD",
            PaymentMethod = request.PaymentMethod ?? "cash",
            SessionId = request.SessionId,
            ThreadId = request.ThreadId,
            Items = request.Items.Select(i => new CommerceLineItem
            {
                Sku = i.Sku,
                Name = i.Name,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity
            }).ToList(),
            Subtotal = calc.Subtotal,
            Discount = calc.Discount,
            Tax = calc.Tax,
            Total = calc.Total,
            State = "sale_created"
        };

        var saved = await _commerce.CreateSaleAsync(doc, ct);
        foreach (var item in request.Items)
        {
            var stock = await _commerce.GetInventoryBySkuAsync(tenantId, item.Sku, ct);
            if (stock?.TracksInventory == true)
                await _commerce.AdjustInventoryAsync(tenantId, item.Sku, -(int)Math.Ceiling(item.Quantity), "sale_created", saved.Id, ct);
        }
        return Ok(new { saved.Id, saved.PartyId, saved.Total, saved.Currency, saved.CreatedAt });
    }

    [HttpGet("sales")]
    public async Task<IActionResult> SearchSales(
        string tenantId,
        [FromQuery] string? partyId = null,
        [FromQuery] string? state = null,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.SalesPos, ct);
        if (moduleCheck is not null) return moduleCheck;
        var items = await _commerce.SearchSalesAsync(tenantId, partyId, state, page, pageSize, ct);
        var total = await _commerce.CountSalesAsync(tenantId, partyId, state, ct);
        return Ok(new PagedResponse<object>
        {
            Items = items.Select(x => new { x.Id, x.PartyId, x.State, x.Subtotal, x.Discount, x.Tax, x.Total, x.Currency, x.PaymentMethod, x.CreatedAt }).ToList(),
            Total = total,
            Page = Math.Max(0, page),
            PageSize = Math.Clamp(pageSize, 1, 100)
        });
    }

    [HttpGet("sales/{saleId}")]
    public async Task<IActionResult> GetSaleById(string tenantId, string saleId, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.SalesPos, ct);
        if (moduleCheck is not null) return moduleCheck;
        var sale = await _commerce.GetSaleByIdAsync(tenantId, saleId, ct);
        return sale is null ? NotFound() : Ok(sale);
    }

    [HttpPut("sales/{saleId}")]
    public async Task<IActionResult> UpdateSale(
        string tenantId,
        string saleId,
        [FromBody] UpdateSaleRequest request,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.SalesPos, ct);
        if (moduleCheck is not null) return moduleCheck;

        var sale = await _commerce.GetSaleByIdAsync(tenantId, saleId, ct);
        if (sale is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var activeSessionCheck = await EnsureActiveSessionIfProvided(tenantId, request.SessionId, ct);
            if (activeSessionCheck is not null)
                return activeSessionCheck;
        }

        var requestedItems = request.Items
            .Select(i => new CommerceLineItem
            {
                Sku = i.Sku,
                Name = i.Name,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity
            })
            .Where(i => i.Quantity > 0)
            .ToList();

        if (requestedItems.Count == 0)
            return BadRequest("Sale must contain at least one item.");

        var previousQuantities = sale.Items
            .GroupBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => (int)Math.Ceiling(x.Sum(i => i.Quantity)), StringComparer.OrdinalIgnoreCase);
        var nextQuantities = requestedItems
            .GroupBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => (int)Math.Ceiling(x.Sum(i => i.Quantity)), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in nextQuantities)
        {
            var previous = previousQuantities.TryGetValue(entry.Key, out var oldQty) ? oldQty : 0;
            var delta = entry.Value - previous;
            if (delta <= 0) continue;

            var stock = await _commerce.GetInventoryBySkuAsync(tenantId, entry.Key, ct);
            if (stock is null) return NotFound($"Inventory SKU '{entry.Key}' not found.");
            if (!stock.Active) return BadRequest($"Inventory SKU '{entry.Key}' is inactive.");
            if (stock.TracksInventory && stock.OnHand < delta)
                return Conflict(new { message = $"Insufficient stock for SKU '{entry.Key}'.", sku = entry.Key, onHand = stock.OnHand, requested = entry.Value });
        }

        var calc = await _commerce.CalculateSaleAsync(
            tenantId,
            requestedItems,
            request.DiscountAmount,
            request.DiscountPercent,
            request.ApplyTax,
            request.TaxRate ?? 0.15m,
            ct);

        foreach (var sku in previousQuantities.Keys.Union(nextQuantities.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var previous = previousQuantities.TryGetValue(sku, out var oldQty) ? oldQty : 0;
            var next = nextQuantities.TryGetValue(sku, out var newQty) ? newQty : 0;
            var delta = next - previous;
            if (delta != 0)
            {
                var stock = await _commerce.GetInventoryBySkuAsync(tenantId, sku, ct);
                if (stock?.TracksInventory == true)
                    await _commerce.AdjustInventoryAsync(tenantId, sku, -delta, "sale_updated", sale.Id, ct);
            }
        }

        sale.Items = requestedItems;
        sale.Subtotal = calc.Subtotal;
        sale.Discount = calc.Discount;
        sale.Tax = calc.Tax;
        sale.Total = calc.Total;
        sale.PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? sale.PaymentMethod : request.PaymentMethod!;
        sale.SessionId = request.SessionId ?? sale.SessionId;
        sale.ThreadId = request.ThreadId ?? sale.ThreadId;
        sale.State = string.IsNullOrWhiteSpace(request.State) ? sale.State : request.State!;

        var updated = await _commerce.UpdateSaleAsync(sale, ct);
        return Ok(updated);
    }

    [HttpPost("sales/calculate")]
    public async Task<IActionResult> CalculateSale(string tenantId, [FromBody] CalculateSaleRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.SalesPos, ct);
        if (moduleCheck is not null) return moduleCheck;
        var calc = await _commerce.CalculateSaleAsync(
            tenantId,
            request.Items.Select(i => new CommerceLineItem { Sku = i.Sku, Name = i.Name, UnitPrice = i.UnitPrice, Quantity = i.Quantity }).ToList(),
            request.DiscountAmount,
            request.DiscountPercent,
            request.ApplyTax,
            request.TaxRate ?? 0.15m,
            ct);
        return Ok(calc);
    }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder(
        string tenantId,
        [FromBody] CreateOrderRequest request,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.SalesPos, ct);
        if (moduleCheck is not null) return moduleCheck;
        var activeSessionCheck = await EnsureActiveSessionIfProvided(tenantId, request.SessionId, ct);
        if (activeSessionCheck is not null)
            return activeSessionCheck;

        var party = await _commerce.GetPartyByIdAsync(tenantId, request.PartyId, ct);
        if (party is null) return NotFound($"Party '{request.PartyId}' not found.");

        var doc = new CommerceOrderDocument
        {
            TenantId = tenantId,
            PartyId = request.PartyId,
            Currency = request.Currency ?? "USD",
            Status = "draft",
            SessionId = request.SessionId,
            ThreadId = request.ThreadId,
            Items = request.Items.Select(i => new CommerceLineItem
            {
                Sku = i.Sku,
                Name = i.Name,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity
            }).ToList(),
            Total = request.Items.Sum(i => i.UnitPrice * i.Quantity)
        };

        var saved = await _commerce.CreateOrderAsync(doc, ct);
        return Ok(new { saved.Id, saved.PartyId, saved.Total, saved.Currency, saved.Status, saved.CreatedAt });
    }

    [HttpGet("orders")]
    public async Task<IActionResult> SearchOrders(
        string tenantId,
        [FromQuery] string? partyId = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.SalesPos, ct);
        if (moduleCheck is not null) return moduleCheck;
        var items = await _commerce.SearchOrdersAsync(tenantId, partyId, status, page, pageSize, ct);
        var total = await _commerce.CountOrdersAsync(tenantId, partyId, status, ct);
        return Ok(new PagedResponse<object>
        {
            Items = items.Select(ToOrderDto).ToList(),
            Total = total,
            Page = Math.Max(0, page),
            PageSize = Math.Clamp(pageSize, 1, 100)
        });
    }

    [HttpGet("orders/{orderId}")]
    public async Task<IActionResult> GetOrderById(string tenantId, string orderId, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.SalesPos, ct);
        if (moduleCheck is not null) return moduleCheck;
        var order = await _commerce.GetOrderByIdAsync(tenantId, orderId, ct);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPut("orders/{orderId}")]
    public async Task<IActionResult> UpdateOrder(string tenantId, string orderId, [FromBody] UpdateOrderRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.SalesPos, ct);
        if (moduleCheck is not null) return moduleCheck;
        var order = await _commerce.GetOrderByIdAsync(tenantId, orderId, ct);
        if (order is null) return NotFound();

        order.Status = string.IsNullOrWhiteSpace(request.Status) ? order.Status : request.Status.Trim();
        order.Notes = string.IsNullOrWhiteSpace(request.Notes) ? order.Notes : request.Notes.Trim();
        if (request.Items is not null && request.Items.Count > 0)
        {
            order.Items = request.Items.Select(i => new CommerceLineItem
            {
                Sku = i.Sku,
                Name = i.Name,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity
            }).ToList();
            order.Total = order.Items.Sum(x => x.Subtotal);
        }

        var updated = await _commerce.UpdateOrderAsync(order, ct);
        return Ok(ToOrderDto(updated));
    }

    [HttpGet("store/settings")]
    public async Task<IActionResult> GetStoreSettings(string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Inventory, ct);
        if (moduleCheck is not null) return moduleCheck;
        var settings = await _commerce.GetStoreSettingsAsync(tenantId, ct);
        return Ok(ToStoreSettingsDto(settings));
    }

    [HttpPut("store/settings")]
    public async Task<IActionResult> UpdateStoreSettings(string tenantId, [FromBody] UpsertStoreSettingsRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Inventory, ct);
        if (moduleCheck is not null) return moduleCheck;

        var current = await _commerce.GetStoreSettingsAsync(tenantId, ct);
        current.StoreName = string.IsNullOrWhiteSpace(request.StoreName) ? current.StoreName : request.StoreName.Trim();
        current.Currency = string.IsNullOrWhiteSpace(request.Currency) ? current.Currency : request.Currency.Trim();
        current.Language = string.IsNullOrWhiteSpace(request.Language) ? current.Language : request.Language.Trim();
        current.TaxRate = request.TaxRate ?? current.TaxRate;
        current.UsePerProductTax = request.UsePerProductTax ?? current.UsePerProductTax;
        current.HideOutOfStockProducts = request.HideOutOfStockProducts ?? current.HideOutOfStockProducts;
        if (request.RegenerateApiToken == true)
            current.ApiToken = Guid.NewGuid().ToString("N");

        var saved = await _commerce.UpsertStoreSettingsAsync(current, ct);
        return Ok(ToStoreSettingsDto(saved));
    }

    [HttpPost("billing/invoices")]
    public async Task<IActionResult> CreateInvoice(
        string tenantId,
        [FromBody] CreateInvoiceRequest request,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Billing, ct);
        if (moduleCheck is not null) return moduleCheck;
        var activeSessionCheck = await EnsureActiveSessionIfProvided(tenantId, request.SessionId, ct);
        if (activeSessionCheck is not null)
            return activeSessionCheck;

        var party = await _commerce.GetPartyByIdAsync(tenantId, request.PartyId, ct);
        if (party is null) return NotFound($"Party '{request.PartyId}' not found.");

        var doc = new CommerceInvoiceDocument
        {
            TenantId = tenantId,
            PartyId = request.PartyId,
            SaleId = request.SaleId,
            OrderId = request.OrderId,
            Currency = request.Currency ?? "USD",
            Total = request.Total,
            Status = "issued",
            Number = $"INV-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            IssuedAt = DateTimeOffset.UtcNow,
            SessionId = request.SessionId,
            ThreadId = request.ThreadId
        };

        var saved = await _commerce.CreateInvoiceAsync(doc, ct);
        if (!string.IsNullOrWhiteSpace(saved.SaleId))
        {
            var sale = await _commerce.GetSaleByIdAsync(tenantId, saved.SaleId, ct);
            if (sale is not null)
            {
                sale.State = "invoiced";
                await _commerce.UpdateSaleAsync(sale, ct);
            }
        }
        return Ok(new { saved.Id, saved.PartyId, saved.SaleId, saved.OrderId, saved.Total, saved.Currency, saved.Status, saved.CreatedAt });
    }

    [HttpGet("billing/invoices")]
    public async Task<IActionResult> SearchInvoices(
        string tenantId,
        [FromQuery] string? partyId = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Billing, ct);
        if (moduleCheck is not null) return moduleCheck;
        var items = await _commerce.SearchInvoicesAsync(tenantId, partyId, status, page, pageSize, ct);
        var total = await _commerce.CountInvoicesAsync(tenantId, partyId, status, ct);
        return Ok(new PagedResponse<object>
        {
            Items = items.Select(x => new { x.Id, x.Number, x.PartyId, x.Status, x.Total, x.Currency, x.SaleId, x.OrderId, x.IssuedAt, x.CreatedAt }).ToList(),
            Total = total,
            Page = Math.Max(0, page),
            PageSize = Math.Clamp(pageSize, 1, 100)
        });
    }

    [HttpGet("billing/invoices/{invoiceId}")]
    public async Task<IActionResult> GetInvoiceById(string tenantId, string invoiceId, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Billing, ct);
        if (moduleCheck is not null) return moduleCheck;
        var invoice = await _commerce.GetInvoiceByIdAsync(tenantId, invoiceId, ct);
        return invoice is null ? NotFound() : Ok(invoice);
    }

    [HttpPut("billing/invoices/{invoiceId}")]
    public async Task<IActionResult> UpdateInvoice(
        string tenantId,
        string invoiceId,
        [FromBody] UpdateInvoiceRequest request,
        CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Billing, ct);
        if (moduleCheck is not null) return moduleCheck;

        var invoice = await _commerce.GetInvoiceByIdAsync(tenantId, invoiceId, ct);
        if (invoice is null) return NotFound();

        invoice.Number = string.IsNullOrWhiteSpace(request.Number) ? invoice.Number : request.Number.Trim();
        invoice.Total = request.Total ?? invoice.Total;
        invoice.Currency = string.IsNullOrWhiteSpace(request.Currency) ? invoice.Currency : request.Currency.Trim();
        invoice.Status = string.IsNullOrWhiteSpace(request.Status) ? invoice.Status : request.Status.Trim();
        invoice.IssuedAt = request.IssuedAt ?? invoice.IssuedAt;

        var updated = await _commerce.UpdateInvoiceAsync(invoice, ct);
        if (string.Equals(updated.Status, "paid", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(updated.SaleId))
        {
            var sale = await _commerce.GetSaleByIdAsync(tenantId, updated.SaleId, ct);
            if (sale is not null)
            {
                sale.State = "paid";
                await _commerce.UpdateSaleAsync(sale, ct);
            }
        }
        return Ok(updated);
    }

    [HttpPut("billing/invoices/{invoiceId}/status")]
    public async Task<IActionResult> UpdateInvoiceStatus(string tenantId, string invoiceId, [FromBody] UpdateInvoiceStatusRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Billing, ct);
        if (moduleCheck is not null) return moduleCheck;
        try
        {
            var updated = await _commerce.UpdateInvoiceStatusAsync(tenantId, invoiceId, request.Status, ct);
            if (string.Equals(updated.Status, "paid", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(updated.SaleId))
            {
                var sale = await _commerce.GetSaleByIdAsync(tenantId, updated.SaleId, ct);
                if (sale is not null)
                {
                    sale.State = "paid";
                    await _commerce.UpdateSaleAsync(sale, ct);
                }
            }
            return Ok(new { updated.Id, updated.Status, updated.SaleId });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("billing/invoices/{invoiceId}/pdf")]
    public async Task<IActionResult> GetInvoicePdf(string tenantId, string invoiceId, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Billing, ct);
        if (moduleCheck is not null) return moduleCheck;
        var invoice = await _commerce.GetInvoiceByIdAsync(tenantId, invoiceId, ct);
        if (invoice is null) return NotFound();
        var party = await _commerce.GetPartyByIdAsync(tenantId, invoice.PartyId, ct);
        var sale = string.IsNullOrWhiteSpace(invoice.SaleId) ? null : await _commerce.GetSaleByIdAsync(tenantId, invoice.SaleId, ct);
        var bytes = InvoicePdfRenderer.Render(invoice, party, sale);
        var fileName = $"{invoice.Number}.pdf";
        return File(bytes, "application/pdf", fileName);
    }

    [HttpPost("billing/invoices/{invoiceId}/send-whatsapp")]
    public async Task<IActionResult> SendInvoiceViaWhatsApp(string tenantId, string invoiceId, CancellationToken ct)
    {
        if (!CanAccess(tenantId)) return Forbid();
        var moduleCheck = await EnsureModuleEnabledAsync(tenantId, CommerceModules.Billing, ct);
        if (moduleCheck is not null) return moduleCheck;
        var invoice = await _commerce.GetInvoiceByIdAsync(tenantId, invoiceId, ct);
        if (invoice is null) return NotFound();
        var party = await _commerce.GetPartyByIdAsync(tenantId, invoice.PartyId, ct);
        if (party is null) return NotFound("Party not found.");
        var sessionId = invoice.SessionId ?? party.LastSessionId;
        if (string.IsNullOrWhiteSpace(sessionId)) return BadRequest("No active/linked session for WhatsApp send.");
        var session = await _sessionRepo.GetByIdAsync(sessionId, tenantId, ct);
        if (session is null) return NotFound("Linked session not found.");
        var outgoing = Domain.Aggregates.ChannelMessage.CreateOutgoing(
            tenantId,
            session.ChannelId,
            session.Id,
            session.Identifier,
            $"Factura {invoice.Number} por {invoice.Total} {invoice.Currency} - estado {invoice.Status}");
        outgoing.Metadata["actor"] = "billing";
        var send = await _channelGateway.SendMessageAsync(session.ChannelId, outgoing, ct);
        if (!send.Success) return BadRequest(new { message = send.Error ?? "Failed to send message." });
        return Ok(new { invoiceId, sent = true, messageId = send.MessageId });
    }

    private bool CanAccess(string tenantId)
    {
        var context = _tenantContext.Current!;
        return context.TenantId == tenantId || context.IsPlatformAdmin;
    }

    private async Task<IActionResult?> EnsureModuleEnabledAsync(string tenantId, string moduleId, CancellationToken ct)
    {
        if (_extensions is null) return null;
        var states = await _extensions.GetTenantExtensionStatesAsync(tenantId, ct);
        if (states.TryGetValue(moduleId, out var enabled) && enabled)
            return null;
        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            message = $"Commerce module '{moduleId}' is not enabled for tenant '{tenantId}'.",
            moduleId
        });
    }

    private async Task<string> ResolveCommercialStateAsync(string tenantId, string? partyId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(partyId)) return "lead";
        var invoices = await _commerce.SearchInvoicesAsync(tenantId, partyId, null, 0, 1, ct);
        var invoice = invoices.FirstOrDefault();
        if (invoice is not null)
        {
            if (string.Equals(invoice.Status, "paid", StringComparison.OrdinalIgnoreCase)) return "paid";
            return "invoiced";
        }
        var sales = await _commerce.SearchSalesAsync(tenantId, partyId, null, 0, 1, ct);
        var sale = sales.FirstOrDefault();
        if (sale is not null) return sale.State;
        var party = await _commerce.GetPartyByIdAsync(tenantId, partyId, ct);
        return string.Equals(party?.Kind, "customer", StringComparison.OrdinalIgnoreCase) ? "lead" : "lead";
    }

    private async Task<IActionResult?> EnsureActiveSessionIfProvided(string tenantId, string? sessionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        var session = await _sessionRepo.GetByIdAsync(sessionId, tenantId, ct);
        if (session is null)
            return NotFound($"Session '{sessionId}' not found.");

        if (session.IsExpired() || session.Status != Domain.Aggregates.SessionStatus.Active)
            return BadRequest(new { message = "Session is not active.", sessionId, session.Status, session.ExpiresAt });

        return null;
    }

    private async Task SyncSessionPartyMetadataAsync(
        string tenantId,
        string? sessionId,
        string channel,
        string identifier,
        CommercePartyDocument party,
        CancellationToken ct)
    {
        ChannelSession? session = null;
        if (!string.IsNullOrWhiteSpace(sessionId))
            session = await _sessionRepo.GetByIdAsync(sessionId, tenantId, ct);

        if (session is null)
        {
            var sessions = await _sessionRepo.GetActiveByUserAsync(identifier, tenantId, ct);
            session = sessions.FirstOrDefault(x =>
                string.Equals(x.ChannelType, channel, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
        }

        if (session is null)
            return;

        session.Metadata["display_name"] = party.DisplayName ?? party.FullName ?? party.Phone ?? party.Identifier;
        session.Metadata["customer_kind"] = party.Kind;
        await _sessionRepo.UpdateAsync(session, ct);
    }

    private static object ToInventoryDto(CommerceInventoryItemDocument item) => new
    {
        item.Id,
        item.Sku,
        item.Name,
        item.Description,
        item.ItemType,
        item.UnitOfMeasure,
        item.TracksInventory,
        item.UnitPrice,
        item.OnHand,
        item.Active,
        item.CategoryIds,
        item.BranchIds,
        item.ImageUrls,
        attributes = item.Attributes.Select(x => new { x.Key, x.Value }).ToList(),
        discount = item.Discount is null ? null : new { item.Discount.Enabled, item.Discount.Type, item.Discount.Value },
        variations = item.Variations.Select(x => new
        {
            x.Id,
            x.Sku,
            x.Name,
            x.Price,
            x.Stock,
            x.Active,
            attributes = x.Attributes.Select(a => new { a.Key, a.Value }).ToList(),
            x.ImageUrls,
            branchStocks = x.BranchStocks.Select(stock => new { stock.BranchId, stock.OnHand }).ToList()
        }).ToList(),
        branchStocks = item.BranchStocks.Select(x => new { x.BranchId, x.OnHand }).ToList()
    };

    private static object ToCategoryDto(CommerceCategoryDocument category) => new
    {
        category.Id,
        category.Name,
        category.Description,
        category.ParentCategoryId,
        category.SortOrder,
        category.Active,
        category.CreatedAt,
        category.UpdatedAt
    };

    private static object ToBranchDto(CommerceBranchDocument branch) => new
    {
        branch.Id,
        branch.Code,
        branch.Name,
        branch.Address,
        branch.Phone,
        branch.Active,
        branch.Properties,
        branch.CreatedAt,
        branch.UpdatedAt
    };

    private static object ToOrderDto(CommerceOrderDocument order) => new
    {
        order.Id,
        order.PartyId,
        order.Currency,
        order.Total,
        order.Status,
        order.Notes,
        order.SessionId,
        order.ThreadId,
        itemsCount = order.Items.Count,
        order.CreatedAt
    };

    private static object ToStoreSettingsDto(CommerceStoreSettingsDocument settings) => new
    {
        settings.StoreName,
        settings.StoreId,
        settings.ApiToken,
        settings.Currency,
        settings.Language,
        settings.TaxRate,
        settings.UsePerProductTax,
        settings.HideOutOfStockProducts,
        settings.CreatedAt,
        settings.UpdatedAt
    };

    private static object ToPartyDto(CommercePartyDocument party) => new
    {
        party.Id,
        party.Kind,
        party.Channel,
        party.Identifier,
        party.DisplayName,
        party.FullName,
        party.Email,
        party.Phone,
        linkedIdentities = party.LinkedIdentities.Select(x => new { x.Channel, x.Identifier }).ToList(),
        party.LastSessionId,
        party.LastThreadId,
        party.CreatedAt,
        party.UpdatedAt
    };
}

internal static class CommerceModules
{
    public const string CommunicationInbox = "communication-inbox";
    public const string Inventory = "inventory";
    public const string SalesPos = "sales-pos";
    public const string Billing = "billing";
}

public sealed record ResolvePartyRequest
{
    public required string Channel { get; init; }
    public required string Identifier { get; init; }
    public string? DisplayName { get; init; }
    public string? FullName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Kind { get; init; }
    public string? SessionId { get; init; }
}

public sealed record UpdateCustomerRequest
{
    public string? DisplayName { get; init; }
    public string? FullName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Kind { get; init; }
}

public sealed record CreateSaleRequest
{
    public required string PartyId { get; init; }
    public string? Currency { get; init; }
    public string? SessionId { get; init; }
    public string? ThreadId { get; init; }
    public string? PaymentMethod { get; init; }
    public bool ApplyTax { get; init; } = true;
    public decimal? TaxRate { get; init; } = 0.15m;
    public decimal? DiscountAmount { get; init; }
    public decimal? DiscountPercent { get; init; }
    public required List<CommerceLineItemRequest> Items { get; init; }
}

public sealed record CreateOrderRequest
{
    public required string PartyId { get; init; }
    public string? Currency { get; init; }
    public string? SessionId { get; init; }
    public string? ThreadId { get; init; }
    public required List<CommerceLineItemRequest> Items { get; init; }
}

public sealed record UpdateSaleRequest
{
    public string? SessionId { get; init; }
    public string? ThreadId { get; init; }
    public string? PaymentMethod { get; init; }
    public bool ApplyTax { get; init; } = true;
    public decimal? TaxRate { get; init; } = 0.15m;
    public decimal? DiscountAmount { get; init; }
    public decimal? DiscountPercent { get; init; }
    public string? State { get; init; }
    public required List<CommerceLineItemRequest> Items { get; init; }
}

public sealed record CreateInvoiceRequest
{
    public required string PartyId { get; init; }
    public string? SaleId { get; init; }
    public string? OrderId { get; init; }
    public required decimal Total { get; init; }
    public string? Currency { get; init; }
    public string? SessionId { get; init; }
    public string? ThreadId { get; init; }
}

public sealed record CommerceLineItemRequest
{
    public required string Sku { get; init; }
    public required string Name { get; init; }
    public required decimal UnitPrice { get; init; }
    public required decimal Quantity { get; init; }
}

public sealed record SendConversationMessageRequest
{
    public required string Content { get; init; }
}

public sealed record UpsertInventoryRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? ItemType { get; init; }
    public string? UnitOfMeasure { get; init; }
    public bool? TracksInventory { get; init; }
    public required decimal UnitPrice { get; init; }
    public required int OnHand { get; init; }
    public bool Active { get; init; } = true;
    public List<string>? CategoryIds { get; init; }
    public List<string>? BranchIds { get; init; }
    public List<string>? ImageUrls { get; init; }
    public List<ProductAttributeRequest>? Attributes { get; init; }
    public ProductDiscountRequest? Discount { get; init; }
    public List<ProductVariationRequest>? Variations { get; init; }
    public List<BranchStockRequest>? BranchStocks { get; init; }
}

public sealed record ProductAttributeRequest
{
    public required string Key { get; init; }
    public required string Value { get; init; }
}

public sealed record ProductDiscountRequest
{
    public bool Enabled { get; init; }
    public string Type { get; init; } = "percent";
    public decimal Value { get; init; }
}

public sealed record ProductVariationRequest
{
    public string? Id { get; init; }
    public required string Sku { get; init; }
    public string? Name { get; init; }
    public decimal Price { get; init; }
    public int Stock { get; init; }
    public bool Active { get; init; } = true;
    public List<ProductAttributeRequest>? Attributes { get; init; }
    public List<string>? ImageUrls { get; init; }
    public List<BranchStockRequest>? BranchStocks { get; init; }
}

public sealed record BranchStockRequest
{
    public required string BranchId { get; init; }
    public int OnHand { get; init; }
}

public sealed record UpsertCategoryRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? ParentCategoryId { get; init; }
    public int? SortOrder { get; init; }
    public bool Active { get; init; } = true;
}

public sealed record UpsertBranchRequest
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public bool Active { get; init; } = true;
    public Dictionary<string, string>? Properties { get; init; }
}

public sealed record AdjustInventoryRequest
{
    public required int Delta { get; init; }
    public required string Reason { get; init; }
    public string? ReferenceId { get; init; }
}

public sealed record CalculateSaleRequest
{
    public required List<CommerceLineItemRequest> Items { get; init; }
    public decimal? DiscountAmount { get; init; }
    public decimal? DiscountPercent { get; init; }
    public bool ApplyTax { get; init; } = true;
    public decimal? TaxRate { get; init; } = 0.15m;
}

public sealed record UpdateInvoiceStatusRequest
{
    public required string Status { get; init; }
}

public sealed record UpdateInvoiceRequest
{
    public string? Number { get; init; }
    public decimal? Total { get; init; }
    public string? Currency { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? IssuedAt { get; init; }
}

public sealed record UpdateOrderRequest
{
    public string? Status { get; init; }
    public string? Notes { get; init; }
    public List<CommerceLineItemRequest>? Items { get; init; }
}

public sealed record UpsertStoreSettingsRequest
{
    public string? StoreName { get; init; }
    public string? Currency { get; init; }
    public string? Language { get; init; }
    public decimal? TaxRate { get; init; }
    public bool? UsePerProductTax { get; init; }
    public bool? HideOutOfStockProducts { get; init; }
    public bool? RegenerateApiToken { get; init; }
}
