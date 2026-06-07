using System.Text.Json;
using System.Text.Json.Nodes;
using CallForMe.Api.Options;
using Microsoft.Extensions.Options;

namespace CallForMe.Api.Services;

public sealed class LocalSettingsWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly IOptionsMonitor<AiOptions> _aiOptions;
    private readonly IOptionsMonitor<TwilioOptions> _twilioOptions;
    private readonly IOptionsMonitor<TonPaymentsOptions> _tonOptions;
    private readonly IOptionsMonitor<UsdtPaymentsOptions> _usdtOptions;

    public LocalSettingsWriter(
        IHostEnvironment environment,
        IOptionsMonitor<AiOptions> aiOptions,
        IOptionsMonitor<TwilioOptions> twilioOptions,
        IOptionsMonitor<TonPaymentsOptions> tonOptions,
        IOptionsMonitor<UsdtPaymentsOptions> usdtOptions)
    {
        _path = Path.Combine(environment.ContentRootPath, "appsettings.Local.json");
        _aiOptions = aiOptions;
        _twilioOptions = twilioOptions;
        _tonOptions = tonOptions;
        _usdtOptions = usdtOptions;
    }

    public async Task SaveOpenAiAsync(string apiKey, string? model, CancellationToken cancellationToken)
    {
        var root = await LoadAsync(cancellationToken);
        var current = _aiOptions.CurrentValue;
        var ai = GetSection(root, AiOptions.SectionName);
        ai["Enabled"] = true;
        ai["ApiKey"] = apiKey;
        ai["Model"] = string.IsNullOrWhiteSpace(model) ? current.Model : model;
        ai["BaseUrl"] = string.IsNullOrWhiteSpace(current.BaseUrl) ? "https://api.openai.com/v1" : current.BaseUrl;
        await SaveAsync(root, cancellationToken);
    }

    public async Task SaveTwilioAsync(
        string accountSid,
        string authToken,
        string fromNumber,
        string publicBaseUrl,
        CancellationToken cancellationToken)
    {
        var root = await LoadAsync(cancellationToken);
        var current = _twilioOptions.CurrentValue;
        var credentialsChanged =
            !string.Equals(accountSid, current.AccountSid, StringComparison.Ordinal) ||
            !string.Equals(authToken, current.AuthToken, StringComparison.Ordinal);
        var twilio = GetSection(root, TwilioOptions.SectionName);
        twilio["Enabled"] = true;
        twilio["AccountSid"] = accountSid;
        twilio["AuthToken"] = authToken;
        twilio["FromNumber"] = fromNumber;
        twilio["PublicBaseUrl"] = publicBaseUrl;
        twilio["ValidateSignatures"] = current.ValidateSignatures;
        if (credentialsChanged)
        {
            twilio["CredentialsValid"] = null;
            twilio["CredentialsError"] = null;
            twilio["CredentialsCheckedAt"] = null;
        }
        await SaveAsync(root, cancellationToken);
    }

    public async Task SaveTwilioCredentialCheckAsync(
        bool isValid,
        string? error,
        CancellationToken cancellationToken)
    {
        var root = await LoadAsync(cancellationToken);
        var twilio = GetSection(root, TwilioOptions.SectionName);
        twilio["CredentialsValid"] = isValid;
        twilio["CredentialsError"] = string.IsNullOrWhiteSpace(error) ? null : error;
        twilio["CredentialsCheckedAt"] = DateTimeOffset.UtcNow;
        await SaveAsync(root, cancellationToken);
    }

    public async Task SaveTonPaymentsAsync(
        string walletAddress,
        decimal creditsPerTon,
        decimal minTonAmount,
        CancellationToken cancellationToken)
    {
        var root = await LoadAsync(cancellationToken);
        var current = _tonOptions.CurrentValue;
        var ton = GetSection(root, TonPaymentsOptions.SectionName);
        ton["Enabled"] = true;
        ton["WalletAddress"] = walletAddress;
        ton["CreditsPerTon"] = creditsPerTon > 0 ? creditsPerTon : current.CreditsPerTon;
        ton["MinTonAmount"] = minTonAmount > 0 ? minTonAmount : current.MinTonAmount;
        await SaveAsync(root, cancellationToken);
    }

    public async Task SaveUsdtPaymentsAsync(
        string walletAddress,
        string network,
        decimal creditsPerUsdt,
        decimal minUsdtAmount,
        CancellationToken cancellationToken)
    {
        var root = await LoadAsync(cancellationToken);
        var current = _usdtOptions.CurrentValue;
        var usdt = GetSection(root, UsdtPaymentsOptions.SectionName);
        usdt["Enabled"] = true;
        usdt["WalletAddress"] = walletAddress;
        usdt["Network"] = string.IsNullOrWhiteSpace(network) ? current.Network : network.Trim();
        usdt["CreditsPerUsdt"] = creditsPerUsdt > 0 ? creditsPerUsdt : current.CreditsPerUsdt;
        usdt["MinUsdtAmount"] = minUsdtAmount > 0 ? minUsdtAmount : current.MinUsdtAmount;
        await SaveAsync(root, cancellationToken);
    }

    private async Task<JsonObject> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new JsonObject();
        }

        await using var stream = File.OpenRead(_path);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
        return node as JsonObject ?? new JsonObject();
    }

    private async Task SaveAsync(JsonObject root, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, root.ToJsonString(JsonOptions), cancellationToken);
    }

    private static JsonObject GetSection(JsonObject root, string name)
    {
        if (root[name] is JsonObject section)
        {
            return section;
        }

        section = new JsonObject();
        root[name] = section;
        return section;
    }
}
