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
    public string FilePath { get; set; } = "data/calls.json";
    public string BillingFilePath { get; set; } = "data/billing.json";
    public string DatabasePath { get; set; } = "data/callforme.db";
}

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string Password { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Password);
}
