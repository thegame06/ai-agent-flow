using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Collections;
using AgentFlow.Api.Commerce;

namespace AgentFlow.Api.Channels;

/// <summary>The registered customer behind an address, so every channel shows and serves the same person.</summary>
public sealed record ResolvedCustomer(string PartyId, string? Name);

public interface ICustomerResolver
{
    Task<ResolvedCustomer?> ResolveAsync(string tenantId, CustomerAddress address, CancellationToken ct = default);
}

/// <summary>
/// Finds the customer by phone: first among registered parties (customers, leads), then in the
/// collections portfolio. Phones compare with <see cref="RecipientKeys"/>, the rule campaigns and
/// collections already use, so the match is the same one those features make.
/// </summary>
public sealed class CustomerResolver : ICustomerResolver
{
    private readonly ICommerceStore? _parties;
    private readonly ICollectionsStore? _collections;

    public CustomerResolver(ICommerceStore? parties = null, ICollectionsStore? collections = null)
    {
        _parties = parties;
        _collections = collections;
    }

    public async Task<ResolvedCustomer?> ResolveAsync(string tenantId, CustomerAddress address, CancellationToken ct = default)
    {
        if (!address.IsPhone || address.Unresolved) return null;
        var key = RecipientKeys.From(address.Value);
        if (key is null) return null;

        if (_parties is not null)
        {
            // Narrow by the last digits in the database, then compare with the shared rule.
            var digits = address.Value.TrimStart('+');
            var tail = digits.Length > 8 ? digits[^8..] : digits;
            var candidates = await _parties.SearchPartiesAsync(tenantId, tail, 0, 20, ct);
            var party = candidates.FirstOrDefault(p => RecipientKeys.From(p.Phone) == key)
                        ?? candidates.FirstOrDefault(p => RecipientKeys.From(p.Identifier) == key);
            if (party is not null)
                return new(party.Id, FirstNonEmpty(party.FullName, party.DisplayName));
        }

        if (_collections is not null)
        {
            var loan = (await _collections.GetLoansAsync(tenantId, ct))
                .FirstOrDefault(l => RecipientKeys.From(l.CustomerPhone) == key);
            if (loan is not null && !string.IsNullOrWhiteSpace(loan.PartyId))
                return new(loan.PartyId, FirstNonEmpty(loan.CustomerName));
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
