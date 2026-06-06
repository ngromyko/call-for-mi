using System.Text.Json;
using CallForMe.Api.Models;
using CallForMe.Api.Options;
using Microsoft.Extensions.Options;

namespace CallForMe.Api.Services;

public interface IBillingRepository
{
    Task<BalanceView> GetBalanceAsync(string clientId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PromoCodeView>> ListPromoCodesAsync(CancellationToken cancellationToken);
    Task<PromoCodeView> CreatePromoCodeAsync(string code, decimal amount, int? maxRedemptions, DateTimeOffset? expiresAt, CancellationToken cancellationToken);
    Task<PromoCodeView?> SetPromoCodeActiveAsync(Guid id, bool active, CancellationToken cancellationToken);
    Task<(BalanceView? Balance, string? Error)> RedeemPromoCodeAsync(string clientId, string code, CancellationToken cancellationToken);
}

public sealed class JsonBillingRepository : IBillingRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public JsonBillingRepository(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        var filePath = string.IsNullOrWhiteSpace(options.Value.BillingFilePath)
            ? "data/billing.json"
            : options.Value.BillingFilePath;
        _path = Path.IsPathRooted(filePath)
            ? filePath
            : Path.Combine(environment.ContentRootPath, filePath);
    }

    public async Task<BalanceView> GetBalanceAsync(string clientId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            return ToBalanceView(GetOrCreateBalance(state, clientId));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PromoCodeView>> ListPromoCodesAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            return state.PromoCodes
                .OrderByDescending(code => code.CreatedAt)
                .Select(code => ToPromoCodeView(state, code))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PromoCodeView> CreatePromoCodeAsync(
        string code,
        decimal amount,
        int? maxRedemptions,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            var normalized = NormalizeCode(code);
            var promoCode = state.PromoCodes.FirstOrDefault(candidate => candidate.Code == normalized);
            if (promoCode is null)
            {
                promoCode = new PromoCode { Code = normalized };
                state.PromoCodes.Add(promoCode);
            }

            promoCode.Amount = amount;
            promoCode.MaxRedemptions = maxRedemptions;
            promoCode.ExpiresAt = expiresAt;
            promoCode.Active = true;
            await SaveAsync(state, cancellationToken);
            return ToPromoCodeView(state, promoCode);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PromoCodeView?> SetPromoCodeActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            var promoCode = state.PromoCodes.FirstOrDefault(code => code.Id == id);
            if (promoCode is null)
            {
                return null;
            }

            promoCode.Active = active;
            await SaveAsync(state, cancellationToken);
            return ToPromoCodeView(state, promoCode);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(BalanceView? Balance, string? Error)> RedeemPromoCodeAsync(
        string clientId,
        string code,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            var normalized = NormalizeCode(code);
            var promoCode = state.PromoCodes.FirstOrDefault(candidate => candidate.Code == normalized);
            if (promoCode is null || !promoCode.Active)
            {
                return (null, "Промокод не найден или уже отключён.");
            }

            if (promoCode.ExpiresAt is not null && promoCode.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                return (null, "Срок действия промокода истёк.");
            }

            if (state.Redemptions.Any(redemption => redemption.ClientId == clientId && redemption.PromoCodeId == promoCode.Id))
            {
                return (null, "Этот промокод уже применён.");
            }

            var redemptionCount = state.Redemptions.Count(redemption => redemption.PromoCodeId == promoCode.Id);
            if (promoCode.MaxRedemptions is not null && redemptionCount >= promoCode.MaxRedemptions)
            {
                return (null, "Лимит активаций промокода уже исчерпан.");
            }

            var balance = GetOrCreateBalance(state, clientId);
            balance.Balance += promoCode.Amount;
            balance.UpdatedAt = DateTimeOffset.UtcNow;
            state.Redemptions.Add(new PromoRedemption
            {
                PromoCodeId = promoCode.Id,
                Code = promoCode.Code,
                ClientId = clientId,
                Amount = promoCode.Amount
            });

            await SaveAsync(state, cancellationToken);
            return (ToBalanceView(balance), null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<BillingState> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new BillingState();
        }

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<BillingState>(stream, _jsonOptions, cancellationToken) ?? new BillingState();
    }

    private async Task SaveAsync(BillingState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, _jsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _path, true);
    }

    private static ClientBalance GetOrCreateBalance(BillingState state, string clientId)
    {
        var balance = state.Balances.FirstOrDefault(candidate => candidate.ClientId == clientId);
        if (balance is not null)
        {
            return balance;
        }

        balance = new ClientBalance { ClientId = clientId };
        state.Balances.Add(balance);
        return balance;
    }

    private static PromoCodeView ToPromoCodeView(BillingState state, PromoCode code) =>
        new(
            code.Id,
            code.Code,
            code.Amount,
            state.Redemptions.Count(redemption => redemption.PromoCodeId == code.Id),
            code.MaxRedemptions,
            code.Active,
            code.ExpiresAt,
            code.CreatedAt);

    private static BalanceView ToBalanceView(ClientBalance balance) => new(balance.ClientId, balance.Balance);

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}
