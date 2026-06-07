namespace CallForMe.Api.Options;

public sealed class TwilioOptions
{
    public const string SectionName = "Twilio";

    public bool Enabled { get; set; }
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string FromNumber { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";
    public bool ValidateSignatures { get; set; } = true;
    public bool? CredentialsValid { get; set; }

    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(AccountSid) &&
        !string.IsNullOrWhiteSpace(AuthToken) &&
        !string.IsNullOrWhiteSpace(FromNumber) &&
        HasUsablePublicBaseUrl;

    public IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        if (!Enabled) missing.Add("Twilio:Enabled");
        if (string.IsNullOrWhiteSpace(AccountSid)) missing.Add("Twilio:AccountSid");
        if (string.IsNullOrWhiteSpace(AuthToken)) missing.Add("Twilio:AuthToken");
        if (string.IsNullOrWhiteSpace(FromNumber)) missing.Add("Twilio:FromNumber");
        if (!HasUsablePublicBaseUrl) missing.Add("Twilio:PublicBaseUrl");
        return missing;
    }

    private bool HasUsablePublicBaseUrl => IsUsablePublicBaseUrl(PublicBaseUrl);

    public static bool IsUsablePublicBaseUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        !uri.Host.Contains("replace-with", StringComparison.OrdinalIgnoreCase) &&
        !uri.Host.EndsWith(".example", StringComparison.OrdinalIgnoreCase) &&
        !uri.IsLoopback;
}

public sealed class AiOptions
{
    public const string SectionName = "AI";

    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-5.4-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ApiKey);

    public IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        if (!Enabled) missing.Add("AI:Enabled");
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add("AI:ApiKey");
        return missing;
    }
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string BillingFilePath { get; set; } = "data/billing.json";
    public string DatabasePath { get; set; } = "data/callforme.db";
    public string LegacyCallsJsonPath { get; set; } = "data/calls.json";
}

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string Password { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Password);
}

public sealed class TonPaymentsOptions
{
    public const string SectionName = "TonPayments";
    private const string ZeroAddress = "EQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    public bool Enabled { get; set; }
    public string WalletAddress { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "https://tonapi.io";
    public decimal CreditsPerTon { get; set; } = 1000m;
    public decimal MinTonAmount { get; set; } = 0.1m;
    public int PollIntervalSeconds { get; set; } = 45;
    public int LookbackLimit { get; set; } = 50;

    public bool IsConfigured =>
        Enabled &&
        IsLikelyTonAddress(WalletAddress) &&
        CreditsPerTon > 0 &&
        MinTonAmount > 0;

    public static bool IsLikelyTonAddress(string? value)
    {
        var address = value?.Trim() ?? "";
        if (address.Equals(ZeroAddress, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return address.Length is >= 32 and <= 128 &&
            address.All(character => char.IsLetterOrDigit(character) || character is '_' or '-');
    }
}

public sealed class UsdtPaymentsOptions
{
    public const string SectionName = "UsdtPayments";
    public const string TetherUsdtTonJettonMaster = "EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs";

    public bool Enabled { get; set; }
    public string WalletAddress { get; set; } = "";
    public string Network { get; set; } = "TON";
    public string JettonMasterAddress { get; set; } = TetherUsdtTonJettonMaster;
    public decimal CreditsPerUsdt { get; set; } = 100m;
    public decimal MinUsdtAmount { get; set; } = 1m;

    public bool IsConfigured =>
        Enabled &&
        (IsTonNetwork ? TonPaymentsOptions.IsLikelyTonAddress(WalletAddress) : IsLikelyWalletAddress(WalletAddress)) &&
        CreditsPerUsdt > 0 &&
        MinUsdtAmount > 0;

    public bool IsTonNetwork => IsTonNetworkValue(Network);

    public static bool IsTonNetworkValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Trim().Equals("TON", StringComparison.OrdinalIgnoreCase) ||
        value.Trim().Equals("TON-USDT", StringComparison.OrdinalIgnoreCase) ||
        value.Trim().Equals("USDT-TON", StringComparison.OrdinalIgnoreCase);

    public static bool IsLikelyWalletAddress(string? value)
    {
        var address = value?.Trim() ?? "";
        return address.Length is >= 24 and <= 128 &&
            address.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or ':');
    }
}
