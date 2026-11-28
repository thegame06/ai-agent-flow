using AgentFlow.Domain.Aggregates;

namespace AgentFlow.Api.Commerce;

/// <summary>
/// Replaces the sales assistant's [REFERENCIA_VENTA] with the reference of the sale just made in
/// this conversation. Models drop or swap characters when they copy ids, so they never write it.
/// </summary>
public sealed class SaleReferenceReplyTransformer : AgentFlow.Core.Engine.IChannelReplyTransformer
{
    public const string Placeholder = "[REFERENCIA_VENTA]";
    private readonly IServiceScopeFactory _scopes;

    public SaleReferenceReplyTransformer(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task<string> TransformAsync(ChannelMessage incoming, ChannelDefinition channel, string reply, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(reply) || !reply.Contains(Placeholder, StringComparison.OrdinalIgnoreCase)) return reply;
        using var scope = _scopes.CreateScope();
        var commerce = scope.ServiceProvider.GetRequiredService<ICommerceStore>();
        var sale = string.IsNullOrWhiteSpace(incoming.SessionId)
            ? null
            : await commerce.GetLatestSaleBySessionAsync(incoming.TenantId, incoming.SessionId, ct);
        return Render(reply, sale);
    }

    public static string Reference(string saleId) => saleId[..Math.Min(8, saleId.Length)].ToUpperInvariant();

    /// <summary>The placeholder becomes the sale's reference; without a sale it is dropped.</summary>
    public static string Render(string reply, CommerceSaleDocument? sale)
        => System.Text.RegularExpressions.Regex.Replace(reply, System.Text.RegularExpressions.Regex.Escape(Placeholder),
            sale is null ? string.Empty : Reference(sale.Id), System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
}
