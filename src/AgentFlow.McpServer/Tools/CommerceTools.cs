using System.Text.Json;
using AgentFlow.McpServer.Client;

namespace AgentFlow.McpServer.Tools;

public sealed class CommerceResolvePartyTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceResolvePartyTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_resolve_party";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Resolves or creates a CRM party (lead/contact) using tenant identity key: tenant + channel + identifier.",
        IntendedFor = "any",
        InputSchemaJson = """
        {"type":"object","required":["channel","identifier"],"properties":{"channel":{"type":"string"},"identifier":{"type":"string"},"displayName":{"type":"string"},"fullName":{"type":"string"},"phone":{"type":"string"},"email":{"type":"string"},"kind":{"type":"string"},"sessionId":{"type":"string"}}}
        """
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var channel = CommerceJson.ReadString(input, "channel");
        var identifier = CommerceJson.ReadString(input, "identifier");
        if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(identifier))
            return McpInvokeResult.Fail(Name, "channel and identifier are required");

        var result = await _api.ResolvePartyAsync(
            req.TenantId,
            channel,
            identifier,
            CommerceJson.ReadString(input, "displayName"),
            CommerceJson.ReadString(input, "kind"),
            CommerceJson.ReadString(input, "phone"),
            CommerceJson.ReadString(input, "email"),
            CommerceJson.ReadString(input, "fullName"),
            CommerceJson.ReadString(input, "sessionId"),
            ct);
        return result is null
            ? McpInvokeResult.Fail(Name, "Could not resolve party")
            : McpInvokeResult.Success(Name, req.TenantId, result, req.ExecutionId);
    }
}

public sealed class CommerceAssertActiveSessionTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceAssertActiveSessionTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_assert_active_session";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Validates session lifecycle from core channel session before sales/billing actions.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["sessionId"],"properties":{"sessionId":{"type":"string"}}}"""
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var sessionId = CommerceJson.ReadString(input, "sessionId");
        if (string.IsNullOrWhiteSpace(sessionId))
            return McpInvokeResult.Fail(Name, "sessionId is required");

        var context = await _api.GetCommerceConversationContextAsync(req.TenantId, sessionId, ct);
        if (context is null)
            return McpInvokeResult.Fail(Name, $"Session '{sessionId}' not found");
        if (context.IsExpired || !string.Equals(context.Status, "Active", StringComparison.OrdinalIgnoreCase))
            return McpInvokeResult.Fail(Name, $"Session '{sessionId}' is not active (status={context.Status})");

        return McpInvokeResult.Success(Name, req.TenantId, context, req.ExecutionId);
    }
}

public sealed class CommerceSearchInventoryTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceSearchInventoryTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_search_inventory";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Searches inventory items by product name, SKU, description, category, attributes, or variation names.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","properties":{"query":{"type":"string"},"limit":{"type":"integer"}}}"""
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var limit = CommerceJson.GetInt32(input, "limit") ?? 20;
        var rows = await _api.SearchInventoryAsync(req.TenantId, CommerceJson.ReadString(input, "query"), limit, ct);
        return McpInvokeResult.Success(Name, req.TenantId, new { count = rows.Count, items = rows }, req.ExecutionId);
    }
}

public sealed class CommerceCreateSaleTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceCreateSaleTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_create_sale";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Creates a POS sale for a resolved party.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["partyId","items"],"properties":{"partyId":{"type":"string"},"currency":{"type":"string"},"sessionId":{"type":"string"},"threadId":{"type":"string"},"items":{"type":"array"}}}"""
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var partyId = CommerceJson.ReadString(input, "partyId");
        var items = CommerceJson.ParseItems(input);
        if (string.IsNullOrWhiteSpace(partyId) || items.Count == 0)
            return McpInvokeResult.Fail(Name, "partyId and at least one item are required");

        var sale = await _api.CreateSaleAsync(req.TenantId, partyId, CommerceJson.ReadString(input, "currency"), CommerceJson.ReadString(input, "sessionId"), CommerceJson.ReadString(input, "threadId"), items, ct);
        return sale is null ? McpInvokeResult.Fail(Name, "Could not create sale") : McpInvokeResult.Success(Name, req.TenantId, sale, req.ExecutionId);
    }
}

public sealed class CommerceCreateOrderTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceCreateOrderTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_create_order";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Creates a sales order for a resolved party.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["partyId","items"],"properties":{"partyId":{"type":"string"},"currency":{"type":"string"},"sessionId":{"type":"string"},"threadId":{"type":"string"},"items":{"type":"array"}}}"""
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var partyId = CommerceJson.ReadString(input, "partyId");
        var items = CommerceJson.ParseItems(input);
        if (string.IsNullOrWhiteSpace(partyId) || items.Count == 0)
            return McpInvokeResult.Fail(Name, "partyId and at least one item are required");

        var order = await _api.CreateOrderAsync(req.TenantId, partyId, CommerceJson.ReadString(input, "currency"), CommerceJson.ReadString(input, "sessionId"), CommerceJson.ReadString(input, "threadId"), items, ct);
        return order is null ? McpInvokeResult.Fail(Name, "Could not create order") : McpInvokeResult.Success(Name, req.TenantId, order, req.ExecutionId);
    }
}

public sealed class CommerceCreateInvoiceTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceCreateInvoiceTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_create_invoice";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Issues an invoice for a sale or order.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["partyId","total"],"properties":{"partyId":{"type":"string"},"saleId":{"type":"string"},"orderId":{"type":"string"},"total":{"type":"number"},"currency":{"type":"string"},"sessionId":{"type":"string"},"threadId":{"type":"string"}}}"""
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var partyId = CommerceJson.ReadString(input, "partyId");
        var total = CommerceJson.GetDecimal(input, "total");
        if (string.IsNullOrWhiteSpace(partyId) || total is null)
            return McpInvokeResult.Fail(Name, "partyId and total are required");

        var invoice = await _api.CreateInvoiceAsync(req.TenantId, partyId, CommerceJson.ReadString(input, "saleId"), CommerceJson.ReadString(input, "orderId"), total.Value, CommerceJson.ReadString(input, "currency"), CommerceJson.ReadString(input, "sessionId"), CommerceJson.ReadString(input, "threadId"), ct);
        return invoice is null ? McpInvokeResult.Fail(Name, "Could not create invoice") : McpInvokeResult.Success(Name, req.TenantId, invoice, req.ExecutionId);
    }
}

public sealed class CommerceSearchCustomersTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceSearchCustomersTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_search_customers";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Search customers/leads with server-side pagination.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","properties":{"query":{"type":"string"},"page":{"type":"integer"},"pageSize":{"type":"integer"}}}"""
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var page = CommerceJson.GetInt32(input, "page") ?? 0;
        var pageSize = CommerceJson.GetInt32(input, "pageSize") ?? 25;
        var result = await _api.SearchCustomersAsync(req.TenantId, CommerceJson.ReadString(input, "query"), page, pageSize, ct);
        return result is null
            ? McpInvokeResult.Fail(Name, "Could not search customers")
            : McpInvokeResult.Success(Name, req.TenantId, result, req.ExecutionId);
    }
}

public sealed class CommerceUpdateCustomerTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceUpdateCustomerTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_update_customer";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Updates customer/lead details.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["partyId"],"properties":{"partyId":{"type":"string"},"displayName":{"type":"string"},"fullName":{"type":"string"},"email":{"type":"string"},"phone":{"type":"string"},"kind":{"type":"string"}}}"""
    };

    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var partyId = CommerceJson.ReadString(input, "partyId");
        if (string.IsNullOrWhiteSpace(partyId)) return McpInvokeResult.Fail(Name, "partyId is required");
        var body = new
        {
            displayName = CommerceJson.ReadString(input, "displayName"),
            fullName = CommerceJson.ReadString(input, "fullName"),
            email = CommerceJson.ReadString(input, "email"),
            phone = CommerceJson.ReadString(input, "phone"),
            kind = CommerceJson.ReadString(input, "kind")
        };
        var result = await _api.UpdateCustomerAsync(req.TenantId, partyId, body, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not update customer") : McpInvokeResult.Success(Name, req.TenantId, result, req.ExecutionId);
    }
}

public sealed class CommerceSearchSalesTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceSearchSalesTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_search_sales";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Searches sales with filters and pagination.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","properties":{"partyId":{"type":"string"},"state":{"type":"string"},"page":{"type":"integer"},"pageSize":{"type":"integer"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var result = await _api.SearchSalesAsync(
            req.TenantId,
            CommerceJson.ReadString(input, "partyId"),
            CommerceJson.ReadString(input, "state"),
            CommerceJson.GetInt32(input, "page") ?? 0,
            CommerceJson.GetInt32(input, "pageSize") ?? 25,
            ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not search sales") : McpInvokeResult.Success(Name, req.TenantId, result, req.ExecutionId);
    }
}

public sealed class CommerceCalculateSaleTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceCalculateSaleTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_calculate_sale";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Calculates sale totals before creating sale.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["items"],"properties":{"items":{"type":"array"},"discountAmount":{"type":"number"},"discountPercent":{"type":"number"},"applyTax":{"type":"boolean"},"taxRate":{"type":"number"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var items = CommerceJson.ParseItems(input);
        if (items.Count == 0) return McpInvokeResult.Fail(Name, "items are required");
        var body = new
        {
            items,
            discountAmount = CommerceJson.GetDecimal(input, "discountAmount"),
            discountPercent = CommerceJson.GetDecimal(input, "discountPercent"),
            applyTax = CommerceJson.GetBool(input, "applyTax") ?? true,
            taxRate = CommerceJson.GetDecimal(input, "taxRate") ?? 0.15m
        };
        var result = await _api.CalculateSaleAsync(req.TenantId, body, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not calculate sale") : McpInvokeResult.Success(Name, req.TenantId, result, req.ExecutionId);
    }
}

public sealed class CommerceUpdateInvoiceStatusTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceUpdateInvoiceStatusTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_update_invoice_status";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Updates invoice status.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["invoiceId","status"],"properties":{"invoiceId":{"type":"string"},"status":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var invoiceId = CommerceJson.ReadString(input, "invoiceId");
        var status = CommerceJson.ReadString(input, "status");
        if (string.IsNullOrWhiteSpace(invoiceId) || string.IsNullOrWhiteSpace(status))
            return McpInvokeResult.Fail(Name, "invoiceId and status are required");
        var result = await _api.UpdateInvoiceStatusAsync(req.TenantId, invoiceId, status, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not update invoice status") : McpInvokeResult.Success(Name, req.TenantId, result, req.ExecutionId);
    }
}

public sealed class CommerceSendInvoiceWhatsAppTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceSendInvoiceWhatsAppTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_send_invoice_whatsapp";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Sends invoice summary over linked WhatsApp session.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["invoiceId"],"properties":{"invoiceId":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var invoiceId = CommerceJson.ReadString(input, "invoiceId");
        if (string.IsNullOrWhiteSpace(invoiceId))
            return McpInvokeResult.Fail(Name, "invoiceId is required");
        var ok = await _api.SendInvoiceWhatsAppAsync(req.TenantId, invoiceId, ct);
        return ok ? McpInvokeResult.Success(Name, req.TenantId, new { invoiceId, sent = true }, req.ExecutionId) : McpInvokeResult.Fail(Name, "Could not send invoice");
    }
}

public sealed class CommerceSendConversationMessageTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceSendConversationMessageTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_send_conversation_message";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Sends a message in an active commercial conversation.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["sessionId","content"],"properties":{"sessionId":{"type":"string"},"content":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var sessionId = CommerceJson.ReadString(input, "sessionId");
        var content = CommerceJson.ReadString(input, "content");
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(content))
            return McpInvokeResult.Fail(Name, "sessionId and content are required");
        var ok = await _api.SendConversationMessageAsync(req.TenantId, sessionId, content, ct);
        return ok ? McpInvokeResult.Success(Name, req.TenantId, new { sessionId, sent = true }, req.ExecutionId) : McpInvokeResult.Fail(Name, "Could not send message");
    }
}

public sealed class CommerceCloseConversationTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceCloseConversationTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_close_conversation";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Closes a commercial conversation session.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["sessionId"],"properties":{"sessionId":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var sessionId = CommerceJson.ReadString(input, "sessionId");
        if (string.IsNullOrWhiteSpace(sessionId))
            return McpInvokeResult.Fail(Name, "sessionId is required");
        var ok = await _api.CloseConversationAsync(req.TenantId, sessionId, ct);
        return ok ? McpInvokeResult.Success(Name, req.TenantId, new { sessionId, closed = true }, req.ExecutionId) : McpInvokeResult.Fail(Name, "Could not close conversation");
    }
}

public sealed class CommerceUpsertInventoryItemTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceUpsertInventoryItemTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_upsert_inventory_item";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Creates or updates a product in inventory, including categories, images, attributes, variations, discounts, and branch stock.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["sku","name","unitPrice"],"properties":{"sku":{"type":"string"},"name":{"type":"string"},"unitPrice":{"type":"number"},"onHand":{"type":"integer"},"active":{"type":"boolean"},"itemType":{"type":"string"},"unitOfMeasure":{"type":"string"},"tracksInventory":{"type":"boolean"},"description":{"type":"string"},"categoryIds":{"type":"array","items":{"type":"string"}},"branchIds":{"type":"array","items":{"type":"string"}},"imageUrls":{"type":"array","items":{"type":"string"}},"attributes":{"type":"array"},"discount":{"type":"object"},"variations":{"type":"array"},"branchStocks":{"type":"array"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var sku = CommerceJson.ReadString(input, "sku");
        var name = CommerceJson.ReadString(input, "name");
        var unitPrice = CommerceJson.GetDecimal(input, "unitPrice");
        if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(name) || unitPrice is null)
            return McpInvokeResult.Fail(Name, "sku, name and unitPrice are required");

        var body = new
        {
            name,
            unitPrice = unitPrice.Value,
            onHand = CommerceJson.GetInt32(input, "onHand") ?? 0,
            active = CommerceJson.GetBool(input, "active") ?? true,
            itemType = CommerceJson.ReadString(input, "itemType"),
            unitOfMeasure = CommerceJson.ReadString(input, "unitOfMeasure"),
            tracksInventory = CommerceJson.GetBool(input, "tracksInventory"),
            description = CommerceJson.ReadString(input, "description"),
            categoryIds = CommerceJson.ReadStringArray(input, "categoryIds"),
            branchIds = CommerceJson.ReadStringArray(input, "branchIds"),
            imageUrls = CommerceJson.ReadStringArray(input, "imageUrls"),
            attributes = CommerceJson.ReadJsonValue(input, "attributes"),
            discount = CommerceJson.ReadJsonValue(input, "discount"),
            variations = CommerceJson.ReadJsonValue(input, "variations"),
            branchStocks = CommerceJson.ReadJsonValue(input, "branchStocks")
        };
        var result = await _api.UpsertInventoryItemAsync(req.TenantId, sku, body, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not upsert inventory item") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceAdjustInventoryTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceAdjustInventoryTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_adjust_inventory";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Adjusts stock for an inventory SKU and records the movement reason.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["sku","delta","reason"],"properties":{"sku":{"type":"string"},"delta":{"type":"integer"},"reason":{"type":"string"},"referenceId":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var sku = CommerceJson.ReadString(input, "sku");
        var delta = CommerceJson.GetInt32(input, "delta");
        var reason = CommerceJson.ReadString(input, "reason");
        if (string.IsNullOrWhiteSpace(sku) || delta is null || string.IsNullOrWhiteSpace(reason))
            return McpInvokeResult.Fail(Name, "sku, delta and reason are required");
        var result = await _api.AdjustInventoryAsync(req.TenantId, sku, new
        {
            delta = delta.Value,
            reason,
            referenceId = CommerceJson.ReadString(input, "referenceId")
        }, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not adjust inventory") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceSearchInventoryMovementsTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceSearchInventoryMovementsTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_search_inventory_movements";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Lists inventory movements with pagination and optional SKU filter.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","properties":{"sku":{"type":"string"},"page":{"type":"integer"},"pageSize":{"type":"integer"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var result = await _api.SearchInventoryMovementsAsync(
            req.TenantId,
            CommerceJson.ReadString(input, "sku"),
            CommerceJson.GetInt32(input, "page") ?? 0,
            CommerceJson.GetInt32(input, "pageSize") ?? 25,
            ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not search inventory movements") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceSearchCategoriesTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceSearchCategoriesTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_search_categories";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Searches product categories with pagination.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","properties":{"query":{"type":"string"},"page":{"type":"integer"},"pageSize":{"type":"integer"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var result = await _api.SearchCategoriesAsync(
            req.TenantId,
            CommerceJson.ReadString(input, "query"),
            CommerceJson.GetInt32(input, "page") ?? 0,
            CommerceJson.GetInt32(input, "pageSize") ?? 25,
            ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not search categories") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceCreateCategoryTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceCreateCategoryTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_create_category";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Creates a product category.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["name"],"properties":{"name":{"type":"string"},"description":{"type":"string"},"parentCategoryId":{"type":"string"},"sortOrder":{"type":"integer"},"active":{"type":"boolean"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var name = CommerceJson.ReadString(input, "name");
        if (string.IsNullOrWhiteSpace(name)) return McpInvokeResult.Fail(Name, "name is required");
        var result = await _api.CreateCategoryAsync(req.TenantId, new
        {
            name,
            description = CommerceJson.ReadString(input, "description"),
            parentCategoryId = CommerceJson.ReadString(input, "parentCategoryId"),
            sortOrder = CommerceJson.GetInt32(input, "sortOrder"),
            active = CommerceJson.GetBool(input, "active") ?? true
        }, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not create category") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceUpdateCategoryTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceUpdateCategoryTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_update_category";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Updates a product category.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["categoryId","name"],"properties":{"categoryId":{"type":"string"},"name":{"type":"string"},"description":{"type":"string"},"parentCategoryId":{"type":"string"},"sortOrder":{"type":"integer"},"active":{"type":"boolean"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var categoryId = CommerceJson.ReadString(input, "categoryId");
        var name = CommerceJson.ReadString(input, "name");
        if (string.IsNullOrWhiteSpace(categoryId) || string.IsNullOrWhiteSpace(name))
            return McpInvokeResult.Fail(Name, "categoryId and name are required");
        var result = await _api.UpdateCategoryAsync(req.TenantId, categoryId, new
        {
            name,
            description = CommerceJson.ReadString(input, "description"),
            parentCategoryId = CommerceJson.ReadString(input, "parentCategoryId"),
            sortOrder = CommerceJson.GetInt32(input, "sortOrder"),
            active = CommerceJson.GetBool(input, "active") ?? true
        }, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not update category") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceDeleteCategoryTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceDeleteCategoryTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_delete_category";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Deletes a product category.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["categoryId"],"properties":{"categoryId":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var categoryId = CommerceJson.ReadString(input, "categoryId");
        if (string.IsNullOrWhiteSpace(categoryId)) return McpInvokeResult.Fail(Name, "categoryId is required");
        var ok = await _api.DeleteCategoryAsync(req.TenantId, categoryId, ct);
        return ok ? McpInvokeResult.Success(Name, req.TenantId, new { categoryId, deleted = true }, req.ExecutionId) : McpInvokeResult.Fail(Name, "Could not delete category");
    }
}

public sealed class CommerceSearchBranchesTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceSearchBranchesTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_search_branches";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Searches store branches with pagination.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","properties":{"query":{"type":"string"},"page":{"type":"integer"},"pageSize":{"type":"integer"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var result = await _api.SearchBranchesAsync(
            req.TenantId,
            CommerceJson.ReadString(input, "query"),
            CommerceJson.GetInt32(input, "page") ?? 0,
            CommerceJson.GetInt32(input, "pageSize") ?? 25,
            ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not search branches") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceCreateBranchTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceCreateBranchTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_create_branch";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Creates a store branch.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["code","name"],"properties":{"code":{"type":"string"},"name":{"type":"string"},"address":{"type":"string"},"phone":{"type":"string"},"active":{"type":"boolean"},"properties":{"type":"object"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var code = CommerceJson.ReadString(input, "code");
        var name = CommerceJson.ReadString(input, "name");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            return McpInvokeResult.Fail(Name, "code and name are required");
        var result = await _api.CreateBranchAsync(req.TenantId, new
        {
            code,
            name,
            address = CommerceJson.ReadString(input, "address"),
            phone = CommerceJson.ReadString(input, "phone"),
            active = CommerceJson.GetBool(input, "active") ?? true,
            properties = CommerceJson.ReadJsonValue(input, "properties")
        }, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not create branch") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceUpdateBranchTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceUpdateBranchTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_update_branch";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Updates a store branch.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["branchId","code","name"],"properties":{"branchId":{"type":"string"},"code":{"type":"string"},"name":{"type":"string"},"address":{"type":"string"},"phone":{"type":"string"},"active":{"type":"boolean"},"properties":{"type":"object"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var branchId = CommerceJson.ReadString(input, "branchId");
        var code = CommerceJson.ReadString(input, "code");
        var name = CommerceJson.ReadString(input, "name");
        if (string.IsNullOrWhiteSpace(branchId) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            return McpInvokeResult.Fail(Name, "branchId, code and name are required");
        var result = await _api.UpdateBranchAsync(req.TenantId, branchId, new
        {
            code,
            name,
            address = CommerceJson.ReadString(input, "address"),
            phone = CommerceJson.ReadString(input, "phone"),
            active = CommerceJson.GetBool(input, "active") ?? true,
            properties = CommerceJson.ReadJsonValue(input, "properties")
        }, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not update branch") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceDeleteBranchTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceDeleteBranchTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_delete_branch";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Deletes a store branch.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["branchId"],"properties":{"branchId":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var branchId = CommerceJson.ReadString(input, "branchId");
        if (string.IsNullOrWhiteSpace(branchId)) return McpInvokeResult.Fail(Name, "branchId is required");
        var ok = await _api.DeleteBranchAsync(req.TenantId, branchId, ct);
        return ok ? McpInvokeResult.Success(Name, req.TenantId, new { branchId, deleted = true }, req.ExecutionId) : McpInvokeResult.Fail(Name, "Could not delete branch");
    }
}

public sealed class CommerceGetSaleTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceGetSaleTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_get_sale";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Gets sale detail by ID.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["saleId"],"properties":{"saleId":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var saleId = CommerceJson.ReadString(input, "saleId");
        if (string.IsNullOrWhiteSpace(saleId)) return McpInvokeResult.Fail(Name, "saleId is required");
        var result = await _api.GetSaleByIdAsync(req.TenantId, saleId, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not get sale") : McpInvokeResult.Success(Name, req.TenantId, result, req.ExecutionId);
    }
}

public sealed class CommerceUpdateSaleTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceUpdateSaleTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_update_sale";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Updates a sale including items, discounts, taxes, payment method, and state.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["saleId","items"],"properties":{"saleId":{"type":"string"},"items":{"type":"array"},"discountAmount":{"type":"number"},"discountPercent":{"type":"number"},"applyTax":{"type":"boolean"},"taxRate":{"type":"number"},"paymentMethod":{"type":"string"},"state":{"type":"string"},"sessionId":{"type":"string"},"threadId":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var saleId = CommerceJson.ReadString(input, "saleId");
        var items = CommerceJson.ParseItems(input);
        if (string.IsNullOrWhiteSpace(saleId) || items.Count == 0)
            return McpInvokeResult.Fail(Name, "saleId and items are required");
        var result = await _api.UpdateSaleAsync(req.TenantId, saleId, new
        {
            items,
            discountAmount = CommerceJson.GetDecimal(input, "discountAmount"),
            discountPercent = CommerceJson.GetDecimal(input, "discountPercent"),
            applyTax = CommerceJson.GetBool(input, "applyTax") ?? true,
            taxRate = CommerceJson.GetDecimal(input, "taxRate") ?? 0.15m,
            paymentMethod = CommerceJson.ReadString(input, "paymentMethod"),
            state = CommerceJson.ReadString(input, "state"),
            sessionId = CommerceJson.ReadString(input, "sessionId"),
            threadId = CommerceJson.ReadString(input, "threadId")
        }, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not update sale") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceSearchOrdersTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceSearchOrdersTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_search_orders";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Searches orders with filters and pagination.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","properties":{"partyId":{"type":"string"},"status":{"type":"string"},"page":{"type":"integer"},"pageSize":{"type":"integer"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var result = await _api.SearchOrdersAsync(
            req.TenantId,
            CommerceJson.ReadString(input, "partyId"),
            CommerceJson.ReadString(input, "status"),
            CommerceJson.GetInt32(input, "page") ?? 0,
            CommerceJson.GetInt32(input, "pageSize") ?? 25,
            ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not search orders") : McpInvokeResult.Success(Name, req.TenantId, result, req.ExecutionId);
    }
}

public sealed class CommerceGetOrderTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceGetOrderTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_get_order";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Gets order detail by ID.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["orderId"],"properties":{"orderId":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var orderId = CommerceJson.ReadString(input, "orderId");
        if (string.IsNullOrWhiteSpace(orderId)) return McpInvokeResult.Fail(Name, "orderId is required");
        var result = await _api.GetOrderByIdAsync(req.TenantId, orderId, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not get order") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceUpdateOrderTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceUpdateOrderTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_update_order";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Updates an order status, notes, or line items.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["orderId"],"properties":{"orderId":{"type":"string"},"status":{"type":"string"},"notes":{"type":"string"},"items":{"type":"array"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var orderId = CommerceJson.ReadString(input, "orderId");
        if (string.IsNullOrWhiteSpace(orderId)) return McpInvokeResult.Fail(Name, "orderId is required");
        var result = await _api.UpdateOrderAsync(req.TenantId, orderId, new
        {
            status = CommerceJson.ReadString(input, "status"),
            notes = CommerceJson.ReadString(input, "notes"),
            items = CommerceJson.ReadJsonValue(input, "items")
        }, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not update order") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceGetStoreSettingsTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceGetStoreSettingsTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_get_store_settings";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Gets store settings for the commerce module.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","properties":{}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var result = await _api.GetStoreSettingsAsync(req.TenantId, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not get store settings") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceUpdateStoreSettingsTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceUpdateStoreSettingsTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_update_store_settings";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Updates store settings including currency, language, taxes, and token regeneration.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","properties":{"storeName":{"type":"string"},"currency":{"type":"string"},"language":{"type":"string"},"taxRate":{"type":"number"},"usePerProductTax":{"type":"boolean"},"hideOutOfStockProducts":{"type":"boolean"},"regenerateApiToken":{"type":"boolean"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var result = await _api.UpdateStoreSettingsAsync(req.TenantId, new
        {
            storeName = CommerceJson.ReadString(input, "storeName"),
            currency = CommerceJson.ReadString(input, "currency"),
            language = CommerceJson.ReadString(input, "language"),
            taxRate = CommerceJson.GetDecimal(input, "taxRate"),
            usePerProductTax = CommerceJson.GetBool(input, "usePerProductTax"),
            hideOutOfStockProducts = CommerceJson.GetBool(input, "hideOutOfStockProducts"),
            regenerateApiToken = CommerceJson.GetBool(input, "regenerateApiToken")
        }, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not update store settings") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceSearchInvoicesTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceSearchInvoicesTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_search_invoices";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Searches invoices with filters and pagination.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","properties":{"partyId":{"type":"string"},"status":{"type":"string"},"page":{"type":"integer"},"pageSize":{"type":"integer"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var result = await _api.SearchInvoicesAsync(
            req.TenantId,
            CommerceJson.ReadString(input, "partyId"),
            CommerceJson.ReadString(input, "status"),
            CommerceJson.GetInt32(input, "page") ?? 0,
            CommerceJson.GetInt32(input, "pageSize") ?? 25,
            ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not search invoices") : McpInvokeResult.Success(Name, req.TenantId, result, req.ExecutionId);
    }
}

public sealed class CommerceGetInvoiceTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceGetInvoiceTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_get_invoice";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Gets invoice detail by ID.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["invoiceId"],"properties":{"invoiceId":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var invoiceId = CommerceJson.ReadString(input, "invoiceId");
        if (string.IsNullOrWhiteSpace(invoiceId)) return McpInvokeResult.Fail(Name, "invoiceId is required");
        var result = await _api.GetInvoiceByIdAsync(req.TenantId, invoiceId, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not get invoice") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceUpdateInvoiceTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceUpdateInvoiceTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_update_invoice";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Updates invoice number, total, currency, status, or issue date.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["invoiceId"],"properties":{"invoiceId":{"type":"string"},"number":{"type":"string"},"total":{"type":"number"},"currency":{"type":"string"},"status":{"type":"string"},"issuedAt":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var invoiceId = CommerceJson.ReadString(input, "invoiceId");
        if (string.IsNullOrWhiteSpace(invoiceId)) return McpInvokeResult.Fail(Name, "invoiceId is required");
        var result = await _api.UpdateInvoiceAsync(req.TenantId, invoiceId, new
        {
            number = CommerceJson.ReadString(input, "number"),
            total = CommerceJson.GetDecimal(input, "total"),
            currency = CommerceJson.ReadString(input, "currency"),
            status = CommerceJson.ReadString(input, "status"),
            issuedAt = CommerceJson.ReadString(input, "issuedAt")
        }, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not update invoice") : McpInvokeResult.Success(Name, req.TenantId, result.Value, req.ExecutionId);
    }
}

public sealed class CommerceGetInvoicePdfTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceGetInvoicePdfTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_get_invoice_pdf";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Gets invoice PDF as base64 payload.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["invoiceId"],"properties":{"invoiceId":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var invoiceId = CommerceJson.ReadString(input, "invoiceId");
        if (string.IsNullOrWhiteSpace(invoiceId)) return McpInvokeResult.Fail(Name, "invoiceId is required");
        var result = await _api.GetInvoicePdfAsync(req.TenantId, invoiceId, ct);
        return result is null
            ? McpInvokeResult.Fail(Name, "Could not get invoice PDF")
            : McpInvokeResult.Success(Name, req.TenantId, new
            {
                invoiceId,
                fileName = result.Value.FileName,
                contentType = result.Value.ContentType,
                base64 = result.Value.Base64Content
            }, req.ExecutionId);
    }
}

public sealed class CommerceGetCustomerTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceGetCustomerTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_get_customer";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Gets customer or lead detail by ID.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["partyId"],"properties":{"partyId":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var partyId = CommerceJson.ReadString(input, "partyId");
        if (string.IsNullOrWhiteSpace(partyId)) return McpInvokeResult.Fail(Name, "partyId is required");
        var result = await _api.GetCustomerByIdAsync(req.TenantId, partyId, ct);
        return result is null ? McpInvokeResult.Fail(Name, "Could not get customer") : McpInvokeResult.Success(Name, req.TenantId, result, req.ExecutionId);
    }
}

public sealed class CommerceDeleteCustomerTool : IAgentFlowMcpTool
{
    private readonly AgentFlowApiClient _api;
    public CommerceDeleteCustomerTool(AgentFlowApiClient api) => _api = api;
    public string Name => "af_commerce_delete_customer";
    public McpToolDescriptor Descriptor => new()
    {
        Name = Name,
        Description = "Deletes a customer or lead.",
        IntendedFor = "any",
        InputSchemaJson = """{"type":"object","required":["partyId"],"properties":{"partyId":{"type":"string"}}}"""
    };
    public async Task<McpInvokeResult> ExecuteAsync(McpInvokeRequest req, CancellationToken ct)
    {
        var input = CommerceJson.Parse(req.InputJson);
        var partyId = CommerceJson.ReadString(input, "partyId");
        if (string.IsNullOrWhiteSpace(partyId)) return McpInvokeResult.Fail(Name, "partyId is required");
        var ok = await _api.DeleteCustomerAsync(req.TenantId, partyId, ct);
        return ok ? McpInvokeResult.Success(Name, req.TenantId, new { partyId, deleted = true }, req.ExecutionId) : McpInvokeResult.Fail(Name, "Could not delete customer");
    }
}

internal static class CommerceJson
{
    public static JsonElement Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return default; }
    }

    public static string? ReadString(JsonElement el, string property)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return null;
        return el.TryGetProperty(property, out var val) ? val.GetString() : null;
    }

    public static int? GetInt32(JsonElement el, string property)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return null;
        return el.TryGetProperty(property, out var val) && val.TryGetInt32(out var parsed) ? parsed : null;
    }

    public static decimal? GetDecimal(JsonElement el, string property)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return null;
        return el.TryGetProperty(property, out var val) && val.TryGetDecimal(out var parsed) ? parsed : null;
    }

    public static bool? GetBool(JsonElement el, string property)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return null;
        if (!el.TryGetProperty(property, out var val)) return null;
        return val.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    public static string[]? ReadStringArray(JsonElement el, string property)
    {
        if (el.ValueKind == JsonValueKind.Undefined || !el.TryGetProperty(property, out var val) || val.ValueKind != JsonValueKind.Array)
            return null;

        return val.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToArray();
    }

    public static object? ReadJsonValue(JsonElement el, string property)
    {
        if (el.ValueKind == JsonValueKind.Undefined || !el.TryGetProperty(property, out var val))
            return null;

        return JsonSerializer.Deserialize<object>(val.GetRawText());
    }

    public static List<CommerceLineItemPayload> ParseItems(JsonElement el)
    {
        var items = new List<CommerceLineItemPayload>();
        if (el.ValueKind == JsonValueKind.Undefined || !el.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return items;

        foreach (var it in arr.EnumerateArray())
        {
            var sku = ReadString(it, "sku");
            var name = ReadString(it, "name");
            var price = GetDecimal(it, "unitPrice");
            var qty = GetDecimal(it, "quantity");
            if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(name) || price is null || qty is null)
                continue;
            items.Add(new CommerceLineItemPayload(sku, name, price.Value, qty.Value));
        }

        return items;
    }
}
