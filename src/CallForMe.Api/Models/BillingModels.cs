namespace CallForMe.Api.Models;

public sealed class BillingState
{
    public List<PromoCode> PromoCodes { get; set; } = [];
    public List<ClientBalance> Balances { get; set; } = [];
    public List<PromoRedemption> Redemptions { get; set; } = [];
    public List<TonPayment> TonPayments { get; set; } = [];
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

public sealed class TonPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ClientId { get; set; }
    public required string ExternalId { get; set; }
    public required string Comment { get; set; }
    public required string WalletAddress { get; set; }
    public string? SenderAddress { get; set; }
    public string Currency { get; set; } = "TON";
    public decimal TonAmount { get; set; }
    public decimal CreditsAmount { get; set; }
    public string Status { get; set; } = "Confirmed";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record PromoCodeView(
    Guid Id,
    string Code,
    decimal Amount,
    int RedemptionCount,
    int? MaxRedemptions,
    bool Active,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PromoRedemptionView> Redemptions);

public sealed record PromoRedemptionView(
    Guid Id,
    Guid PromoCodeId,
    string Code,
    string ClientId,
    decimal Amount,
    DateTimeOffset CreatedAt);

public sealed record BalanceView(string ClientId, decimal Balance);

public sealed record TonPaymentView(
    Guid Id,
    string ClientId,
    string ExternalId,
    string Comment,
    string WalletAddress,
    string? SenderAddress,
    string Currency,
    decimal TonAmount,
    decimal CreditsAmount,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ReceivedAt);
