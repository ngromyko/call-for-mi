using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CallForMe.Api.Models;
using CallForMe.Api.Options;
using Microsoft.Extensions.Options;

namespace CallForMe.Api.Services;

public sealed class TonDepositMonitor(
    TonApiClient tonApi,
    IBillingRepository billing,
    IOptionsMonitor<TonPaymentsOptions> options,
    ILogger<TonDepositMonitor> logger) : BackgroundService
{
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    public async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        var current = options.CurrentValue;
        if (!current.IsConfigured)
        {
            return;
        }

        if (!await _checkGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var deposits = await tonApi.GetIncomingTransfersAsync(current, cancellationToken);
            foreach (var deposit in deposits)
            {
                var clientId = TonDepositComment.ToClientId(deposit.Comment);
                if (clientId is null)
                {
                    continue;
                }

                var credits = decimal.Round(deposit.TonAmount * current.CreditsPerTon, 2);
                if (credits <= 0)
                {
                    continue;
                }

                var result = await billing.RecordTonPaymentAsync(
                    deposit.ExternalId,
                    clientId,
                    deposit.Comment,
                    current.WalletAddress.Trim(),
                    deposit.SenderAddress,
                    deposit.TonAmount,
                    credits,
                    deposit.ReceivedAt,
                    cancellationToken);
                if (result.Created)
                {
                    logger.LogInformation("Credited TON deposit {ExternalId} for {ClientId}: {TonAmount} TON -> {CreditsAmount}",
                        deposit.ExternalId,
                        clientId,
                        deposit.TonAmount,
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

            var interval = Math.Clamp(options.CurrentValue.PollIntervalSeconds, 15, 3600);
            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
        }
    }
}

public sealed class TonApiClient(HttpClient httpClient, ILogger<TonApiClient> logger)
{
    public async Task<IReadOnlyList<TonIncomingTransfer>> GetIncomingTransfersAsync(
        TonPaymentsOptions options,
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
                if (!type.Contains("TonTransfer", StringComparison.OrdinalIgnoreCase) &&
                    !type.Contains("ton_transfer", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryGetTonTransfer(action, out var transfer))
                {
                    continue;
                }

                var sender = GetAddress(transfer, "sender", "source");
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
                    comment,
                    amount,
                    sender,
                    timestamp));
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

    private static string NormalizeAddress(string address) => address.Trim();
}

public sealed record TonIncomingTransfer(
    string ExternalId,
    string Comment,
    decimal TonAmount,
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
