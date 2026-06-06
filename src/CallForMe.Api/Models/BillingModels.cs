namespace CallForMe.Api.Models;

public sealed class BillingState
{
    public List<PromoCode> PromoCodes { get; set; } = [];
    public List<ClientBalance> Balances { get; set; } = [];
    public List<PromoRedemption> Redemptions { get; set; } = [];
}

public sealed class PromoCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public decimal Amount { get; set; }
    public int? MaxRedemptions { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ClientBalance
{
    public required string ClientId { get; set; }
    public decimal Balance { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PromoRedemption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid PromoCodeId { get; set; }
    public required string Code { get; set; }
    public required string ClientId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record PromoCodeView(
    Guid Id,
    string Code,
    decimal Amount,
    int RedemptionCount,
    int? MaxRedemptions,
    bool Active,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record BalanceView(string ClientId, decimal Balance);
