using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CallForMe.Api.Models;
using CallForMe.Api.Options;
using Microsoft.Extensions.Options;

namespace CallForMe.Api.Services;

public sealed class TonDepositMonitor(
    TonApiClient tonApi,
    TonPriceClient tonPrice,
    IBillingRepository billing,
    IOptionsMonitor<TonPaymentsOptions> tonOptions,
    IOptionsMonitor<UsdtPaymentsOptions> usdtOptions,
    ILogger<TonDepositMonitor> logger) : BackgroundService
{
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    public async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        var ton = tonOptions.CurrentValue;
        var usdt = usdtOptions.CurrentValue;
        var scanOptions = BuildScanOptions(ton, usdt);
        if (scanOptions is null)
        {
            return;
        }

        if (!await _checkGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var deposits = await tonApi.GetIncomingTransfersAsync(scanOptions, cancellationToken);
            foreach (var deposit in deposits)
            {
                if (!IsEnabledDeposit(deposit, ton, usdt))
                {
                    continue;
                }

                var clientId = TonDepositComment.ToClientId(deposit.Comment);
                if (clientId is null)
                {
                    continue;
                }

                var creditsPerUnit = deposit.Currency == "USDT"
                    ? usdt.CreditsPerUsdt
                    : await tonPrice.GetTonUsdPriceAsync(ton.CreditsPerTon, cancellationToken);
                var credits = decimal.Round(deposit.Amount * creditsPerUnit, 2);
                if (credits <= 0)
                {
                    continue;
                }

                var result = await billing.RecordTonPaymentAsync(
                    deposit.ExternalId,
                    clientId,
                    deposit.Comment,
                    scanOptions.WalletAddress,
                    deposit.SenderAddress,
                    deposit.Currency,
                    deposit.Amount,
                    credits,
                    deposit.ReceivedAt,
                    cancellationToken);
                if (result.Created)
                {
                    logger.LogInformation("Credited {Currency} deposit {ExternalId} for {ClientId}: {Amount} {AssetCurrency} -> {CreditsAmount}",
                        deposit.Currency,
                        deposit.ExternalId,
                        clientId,
                        deposit.Amount,
                        deposit.Currency,
                        credits);
                }
            }
        }
        finally
        {
            _checkGate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "TON deposit check failed.");
            }

            var interval = Math.Clamp(tonOptions.CurrentValue.PollIntervalSeconds, 15, 3600);
            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
        }
    }

    private static TonApiScanOptions? BuildScanOptions(TonPaymentsOptions ton, UsdtPaymentsOptions usdt)
    {
        var wallet = ton.IsConfigured
            ? ton.WalletAddress.Trim()
            : usdt.IsConfigured && usdt.IsTonNetwork
                ? usdt.WalletAddress.Trim()
                : "";
        if (string.IsNullOrWhiteSpace(wallet))
        {
            return null;
        }

        var usdtJettonMaster = usdt.IsConfigured && usdt.IsTonNetwork
            ? (string.IsNullOrWhiteSpace(usdt.JettonMasterAddress)
                ? UsdtPaymentsOptions.TetherUsdtTonJettonMaster
                : usdt.JettonMasterAddress.Trim())
            : "";
        return new TonApiScanOptions(
            wallet,
            string.IsNullOrWhiteSpace(ton.ApiBaseUrl) ? "https://tonapi.io" : ton.ApiBaseUrl,
            ton.ApiKey,
            ton.LookbackLimit,
            usdtJettonMaster);
    }

    private static bool IsEnabledDeposit(TonIncomingTransfer deposit, TonPaymentsOptions ton, UsdtPaymentsOptions usdt) =>
        deposit.Currency switch
        {
            "TON" => ton.IsConfigured,
            "USDT" => usdt.IsConfigured && usdt.IsTonNetwork,
            _ => false
        };
}

public sealed class TonPriceClient(HttpClient httpClient, ILogger<TonPriceClient> logger)
{
    private static readonly Uri PriceUri = new("https://api.coingecko.com/api/v3/simple/price?ids=the-open-network&vs_currencies=usd");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private decimal? _cachedUsdPrice;
    private DateTimeOffset _cachedAt;

    public async Task<decimal> GetTonUsdPriceAsync(decimal fallbackUsdPrice, CancellationToken cancellationToken)
    {
        if (_cachedUsdPrice is > 0 && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromMinutes(2))
        {
            return _cachedUsdPrice.Value;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedUsdPrice is > 0 && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromMinutes(2))
            {
                return _cachedUsdPrice.Value;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, PriceUri);
            request.Headers.UserAgent.ParseAdd("call-for-me/1.0");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("TON price API returned HTTP {StatusCode}.", (int)response.StatusCode);
                return fallbackUsdPrice;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("the-open-network", out var coin) &&
                coin.TryGetProperty("usd", out var usd) &&
                usd.TryGetDecimal(out var price) &&
                price > 0)
            {
                _cachedUsdPrice = price;
                _cachedAt = DateTimeOffset.UtcNow;
                return price;
            }

            logger.LogWarning("TON price API returned an unexpected response shape.");
            return fallbackUsdPrice;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "TON price lookup failed.");
            return fallbackUsdPrice;
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class TonApiClient(HttpClient httpClient, ILogger<TonApiClient> logger)
{
    public async Task<IReadOnlyList<TonIncomingTransfer>> GetIncomingTransfersAsync(
        TonApiScanOptions options,
        CancellationToken cancellationToken)
    {
        var baseUrl = string.IsNullOrWhiteSpace(options.ApiBaseUrl) ? "https://tonapi.io" : options.ApiBaseUrl.TrimEnd('/');
        var limit = Math.Clamp(options.LookbackLimit, 10, 100);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl}/v2/accounts/{Uri.EscapeDataString(options.WalletAddress.Trim())}/events?limit={limit}");
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.Authorization = new("Bearer", options.ApiKey.Trim());
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("TON API returned HTTP {StatusCode}.", (int)response.StatusCode);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var transfers = new List<TonIncomingTransfer>();
        foreach (var item in events.EnumerateArray())
        {
            var eventId = GetString(item, "event_id", "eventId", "id") ?? "";
            var timestamp = GetUnixTime(item, "timestamp", "utime") ?? DateTimeOffset.UtcNow;
            if (!item.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var actionIndex = 0;
            foreach (var action in actions.EnumerateArray())
            {
                actionIndex++;
                var type = GetString(action, "type", "action_type", "actionType") ?? "";
                if (IsTonTransferAction(type))
                {
                    if (!TryGetTonTransfer(action, out var transfer))
                    {
                        continue;
                    }

                    var sender = GetAddress(transfer, "sender", "source", "from", "sender_address", "senderAddress");
                    if (!string.IsNullOrWhiteSpace(sender) &&
                        string.Equals(NormalizeAddress(sender), NormalizeAddress(options.WalletAddress), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var comment = GetString(transfer, "comment", "memo", "text")?.Trim();
                    if (string.IsNullOrWhiteSpace(comment) || TonDepositComment.ToClientId(comment) is null)
                    {
                        continue;
                    }

                    var amount = GetNanoAmount(transfer, "amount", "value");
                    if (amount <= 0)
                    {
                        continue;
                    }

                    var externalId = string.IsNullOrWhiteSpace(eventId)
                        ? $"{comment}:{amount}:{timestamp.ToUnixTimeSeconds()}"
                        : $"{eventId}:{actionIndex}";
                    transfers.Add(new TonIncomingTransfer(
                        externalId,
                        "TON",
                        comment,
                        amount,
                        sender,
                        timestamp));
                    continue;
                }

                if (IsJettonTransferAction(type) &&
                    !string.IsNullOrWhiteSpace(options.UsdtJettonMasterAddress) &&
                    TryGetJettonTransfer(action, out var jettonTransfer))
                {
                    var sender = GetAddress(jettonTransfer, "sender", "source", "from", "sender_address", "senderAddress");
                    var recipient = GetAddress(jettonTransfer, "recipient", "destination", "to", "receiver", "recipient_address", "recipientAddress");
                    if (!string.IsNullOrWhiteSpace(sender) &&
                        string.Equals(NormalizeAddress(sender), NormalizeAddress(options.WalletAddress), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(recipient) &&
                        !string.Equals(NormalizeAddress(recipient), NormalizeAddress(options.WalletAddress), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var jettonMaster = GetAddress(jettonTransfer, "jetton", "asset", "token", "jetton_master", "jettonMaster");
                    if (!IsMatchingAddress(jettonMaster, options.UsdtJettonMasterAddress))
                    {
                        continue;
                    }

                    var comment = GetString(jettonTransfer, "comment", "memo", "text")?.Trim();
                    if (string.IsNullOrWhiteSpace(comment) || TonDepositComment.ToClientId(comment) is null)
                    {
                        continue;
                    }

                    var amount = GetJettonAmount(jettonTransfer, GetJettonDecimals(jettonTransfer), "amount", "value");
                    if (amount <= 0)
                    {
                        continue;
                    }

                    var externalId = string.IsNullOrWhiteSpace(eventId)
                        ? $"USDT:{comment}:{amount}:{timestamp.ToUnixTimeSeconds()}"
                        : $"USDT:{eventId}:{actionIndex}";
                    transfers.Add(new TonIncomingTransfer(
                        externalId,
                        "USDT",
                        comment,
                        amount,
                        sender,
                        timestamp));
                }
            }
        }

        return transfers;
    }

    private static bool TryGetTonTransfer(JsonElement action, out JsonElement transfer)
    {
        foreach (var name in new[] { "TonTransfer", "ton_transfer", "tonTransfer" })
        {
            if (action.TryGetProperty(name, out transfer) && transfer.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
        }

        transfer = action;
        return true;
    }

    private static bool TryGetJettonTransfer(JsonElement action, out JsonElement transfer)
    {
        foreach (var name in new[] { "JettonTransfer", "jetton_transfer", "jettonTransfer" })
        {
            if (action.TryGetProperty(name, out transfer) && transfer.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
        }

        transfer = action;
        return true;
    }

    private static bool IsTonTransferAction(string type) =>
        string.Equals(type, "TonTransfer", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "ton_transfer", StringComparison.OrdinalIgnoreCase);

    private static bool IsJettonTransferAction(string type) =>
        string.Equals(type, "JettonTransfer", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "jetton_transfer", StringComparison.OrdinalIgnoreCase);

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string? GetAddress(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind == JsonValueKind.Object)
            {
                var address = GetString(value, "address", "account", "account_id", "accountId");
                if (!string.IsNullOrWhiteSpace(address))
                {
                    return address;
                }
            }
        }

        return null;
    }

    private static DateTimeOffset? GetUnixTime(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unix))
            {
                return DateTimeOffset.FromUnixTimeSeconds(unix);
            }
        }

        return null;
    }

    private static decimal GetNanoAmount(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            {
                return number / 1_000_000_000m;
            }

            if (value.ValueKind == JsonValueKind.String &&
                decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed > 1_000_000m ? parsed / 1_000_000_000m : parsed;
            }
        }

        return 0;
    }

    private static int GetJettonDecimals(JsonElement transfer)
    {
        var decimals = GetInt(transfer, "decimals") ?? 6;
        foreach (var name in new[] { "jetton", "asset", "token" })
        {
            if (transfer.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
            {
                decimals = GetInt(value, "decimals") ?? decimals;
            }
        }

        return Math.Clamp(decimals, 0, 18);
    }

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal GetJettonAmount(JsonElement element, int decimals, params string[] names)
    {
        var factor = Pow10(decimals);
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt64(out var number))
                {
                    return number / factor;
                }

                if (value.TryGetDecimal(out var decimalNumber))
                {
                    return decimalNumber;
                }
            }

            if (value.ValueKind == JsonValueKind.String &&
                decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return value.GetString()?.Contains('.', StringComparison.Ordinal) == true
                    ? parsed
                    : parsed / factor;
            }
        }

        return 0;
    }

    private static decimal Pow10(int decimals)
    {
        var result = 1m;
        for (var index = 0; index < decimals; index++)
        {
            result *= 10m;
        }

        return result;
    }

    private static bool IsMatchingAddress(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(NormalizeAddress(left), NormalizeAddress(right), StringComparison.Ordinal);

    private static string NormalizeAddress(string address)
    {
        var value = address.Trim();
        if (value.Contains(':', StringComparison.Ordinal))
        {
            var parts = value.Split(':', 2);
            return parts.Length == 2
                ? $"{parts[0]}:{parts[1].ToLowerInvariant()}"
                : value.ToLowerInvariant();
        }

        var raw = TryFriendlyAddressToRaw(value);
        return raw ?? value.ToLowerInvariant();
    }

    private static string? TryFriendlyAddressToRaw(string address)
    {
        try
        {
            var normalized = address.Replace('-', '+').Replace('_', '/');
            var padding = normalized.Length % 4;
            if (padding > 0)
            {
                normalized = normalized.PadRight(normalized.Length + 4 - padding, '=');
            }

            var bytes = Convert.FromBase64String(normalized);
            if (bytes.Length != 36)
            {
                return null;
            }

            var workchain = unchecked((sbyte)bytes[1]);
            var hash = Convert.ToHexString(bytes.AsSpan(2, 32)).ToLowerInvariant();
            return $"{workchain}:{hash}";
        }
        catch
        {
            return null;
        }
    }
}

public sealed record TonApiScanOptions(
    string WalletAddress,
    string ApiBaseUrl,
    string ApiKey,
    int LookbackLimit,
    string UsdtJettonMasterAddress);

public sealed record TonIncomingTransfer(
    string ExternalId,
    string Currency,
    string Comment,
    decimal Amount,
    string? SenderAddress,
    DateTimeOffset ReceivedAt);

public static partial class TonDepositComment
{
    private static readonly Regex CommentPattern = TonDepositRegex();

    public static string FromClientId(string clientId) => $"CFM-{clientId}";

    public static string? ToClientId(string comment)
    {
        var match = CommentPattern.Match(comment.Trim());
        return match.Success ? match.Groups["clientId"].Value : null;
    }

    [GeneratedRegex("^CFM-(?<clientId>[A-Za-z0-9_-]{3,96})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TonDepositRegex();
}
