using System.Text.RegularExpressions;

namespace CallForMe.Api.Contracts;

public sealed record CreateCallRequest(
    string PhoneNumber,
    string Prompt,
    string? DisplayName = null,
    string? Language = "en-US",
    string? UserLanguage = "ru-RU",
    bool AutoPilot = true,
    bool RecordingConsentConfirmed = false);

public sealed record SendMessageRequest(string Text, string? SpokenText = null);
public sealed record SetAutoPilotRequest(bool Enabled);
public sealed record CreatePromoCodeRequest(string Code, decimal Amount, int? MaxRedemptions = null, DateTimeOffset? ExpiresAt = null);
public sealed record RedeemPromoCodeRequest(string ClientId, string Code);
public sealed record SetPromoCodeActiveRequest(bool Active);

public static partial class RequestValidation
{
    public static Dictionary<string, string[]> Validate(CreateCallRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.PhoneNumber) || !E164Regex().IsMatch(request.PhoneNumber.Trim()))
        {
            errors["phoneNumber"] = ["Use E.164 format, for example +48123456789."];
        }

        if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Trim().Length < 10)
        {
            errors["prompt"] = ["Prompt must contain at least 10 characters."];
        }

        return errors;
    }

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164Regex();
}
