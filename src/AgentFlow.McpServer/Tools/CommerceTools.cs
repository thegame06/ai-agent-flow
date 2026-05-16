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
        Description = "Searches inventory items by name or SKU.",
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
