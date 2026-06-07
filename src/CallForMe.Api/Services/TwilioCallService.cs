using System.Net.Http.Headers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CallForMe.Api.Models;
using CallForMe.Api.Options;
using Microsoft.Extensions.Options;

namespace CallForMe.Api.Services;

public sealed class TwilioCallService
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<TwilioOptions> _options;

    public TwilioCallService(HttpClient httpClient, IOptionsMonitor<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public bool IsConfigured => _options.CurrentValue.IsConfigured;

    public async Task<string> StartCallAsync(CallSession call, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Twilio is not configured. Set Twilio:Enabled, AccountSid, AuthToken, FromNumber, and PublicBaseUrl before starting real calls.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{options.AccountSid}/Calls.json");
        request.Headers.Authorization = CreateBasicAuthentication(options);
        request.Content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("To", call.PhoneNumber),
            new KeyValuePair<string, string>("From", options.FromNumber),
            new KeyValuePair<string, string>("Url", $"{PublicBaseUrl(options)}/twilio/voice?callId={call.Id}"),
            new KeyValuePair<string, string>("Method", "POST"),
            new KeyValuePair<string, string>("StatusCallback", $"{PublicBaseUrl(options)}/twilio/status?callId={call.Id}"),
            new KeyValuePair<string, string>("StatusCallbackMethod", "POST"),
            new KeyValuePair<string, string>("StatusCallbackEvent", "initiated"),
            new KeyValuePair<string, string>("StatusCallbackEvent", "ringing"),
            new KeyValuePair<string, string>("StatusCallbackEvent", "answered"),
            new KeyValuePair<string, string>("StatusCallbackEvent", "completed")
        ]);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadTwilioResponseAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw TwilioApiException.FromResponse(response, payload);
        }

        if (string.IsNullOrWhiteSpace(payload.Sid))
        {
            throw new TwilioApiException(
                "Twilio response did not include Call SID.",
                (int)response.StatusCode,
                payload.Code,
                payload.MoreInfo);
        }

        return payload.Sid;
    }

    public async Task CheckCredentialsAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Twilio is not configured. Set Twilio:Enabled, AccountSid, AuthToken, FromNumber, and PublicBaseUrl before checking credentials.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.twilio.com/2010-04-01/Accounts/{options.AccountSid}.json");
        request.Headers.Authorization = CreateBasicAuthentication(options);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadTwilioResponseAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw TwilioApiException.FromResponse(response, payload);
        }
    }

    public async Task EndCallAsync(CallSession call, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!IsConfigured || string.IsNullOrWhiteSpace(call.TwilioCallSid))
        {
            return;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{options.AccountSid}/Calls/{call.TwilioCallSid}.json");
        request.Headers.Authorization = CreateBasicAuthentication(options);
        request.Content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Status", "completed")
        ]);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<TwilioCallCost?> GetCallCostAsync(string callSid, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!IsConfigured || string.IsNullOrWhiteSpace(callSid))
        {
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.twilio.com/2010-04-01/Accounts/{options.AccountSid}/Calls/{callSid}.json");
        request.Headers.Authorization = CreateBasicAuthentication(options);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var payload = ParseTwilioApiResponse(json, response.IsSuccessStatusCode);
            throw TwilioApiException.FromResponse(response, payload);
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var price = GetString(root, "price", "Price");
        if (string.IsNullOrWhiteSpace(price) ||
            !decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedPrice))
        {
            return null;
        }

        return new TwilioCallCost(
            parsedPrice,
            GetString(root, "price_unit", "PriceUnit") ?? "USD",
            GetString(root, "duration", "Duration"),
            GetString(root, "status", "Status"));
    }

    private static AuthenticationHeaderValue CreateBasicAuthentication(TwilioOptions options) =>
        new("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}")));

    private static string PublicBaseUrl(TwilioOptions options) => options.PublicBaseUrl.TrimEnd('/');

    private static async Task<TwilioApiResponse> ReadTwilioResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new TwilioApiResponse(null, null, null, null, null);
        }

        try
        {
            return ParseTwilioApiResponse(json, response.IsSuccessStatusCode);
        }
        catch (JsonException)
        {
            return new TwilioApiResponse(null, response.IsSuccessStatusCode ? null : json, null, null, null);
        }
    }

    private static TwilioApiResponse ParseTwilioApiResponse(string json, bool success)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new TwilioApiResponse(null, null, null, null, null);
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new TwilioApiResponse(
            GetString(root, "sid", "Sid"),
            GetString(root, "message", "Message"),
            GetInt(root, "code", "Code"),
            GetString(root, "more_info", "moreInfo", "MoreInfo"),
            GetInt(root, "status", "Status"));
    }

    private static string? GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }
        }

        return null;
    }

    private static int? GetInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value))
            {
                return value;
            }

            if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value))
            {
                return value;
            }
        }

        return null;
    }

    internal sealed record TwilioApiResponse(
        string? Sid,
        string? Message,
        int? Code,
        string? MoreInfo,
        int? Status);
}

public sealed record TwilioCallCost(
    decimal Price,
    string PriceUnit,
    string? Duration,
    string? Status);

public sealed class TwilioApiException : Exception
{
    public TwilioApiException(string message, int statusCode, int? twilioCode, string? moreInfo)
        : base(message)
    {
        StatusCode = statusCode;
        TwilioCode = twilioCode;
        MoreInfo = moreInfo;
    }

    public int StatusCode { get; }
    public int? TwilioCode { get; }
    public string? MoreInfo { get; }

    internal static TwilioApiException FromResponse(HttpResponseMessage response, TwilioCallService.TwilioApiResponse payload)
    {
        var message = payload.Message;
        var code = payload.Code;
        var moreInfo = payload.MoreInfo;
        var statusCode = (int)response.StatusCode;

        if (statusCode == StatusCodes.Status401Unauthorized ||
            string.Equals(message, "Authenticate", StringComparison.OrdinalIgnoreCase))
        {
            message = "Twilio не принял Account SID/Auth Token. Проверьте, что Auth Token взят из того же Twilio project/account, что и Account SID.";
        }
        else if (string.IsNullOrWhiteSpace(message))
        {
            message = $"Twilio returned HTTP {statusCode}.";
        }

        return new TwilioApiException(message, statusCode, code, moreInfo);
    }
}

public sealed class TwilioRequestValidator(IOptionsMonitor<TwilioOptions> options)
{
    public bool IsValid(HttpRequest request, IFormCollection form)
    {
        var current = options.CurrentValue;
        if (!current.IsConfigured || !current.ValidateSignatures)
        {
            return true;
        }

        var signature = request.Headers["X-Twilio-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var publicUrl = $"{current.PublicBaseUrl.TrimEnd('/')}{request.Path}{request.QueryString}";
        var data = new StringBuilder(publicUrl);
        foreach (var item in form.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            foreach (var value in item.Value.OrderBy(value => value, StringComparer.Ordinal))
            {
                data.Append(item.Key).Append(value);
            }
        }

        return HasValidSignature(data.ToString(), signature, current.AuthToken);
    }

    public bool IsValidWebSocket(HttpRequest request)
    {
        var current = options.CurrentValue;
        if (!current.IsConfigured || !current.ValidateSignatures)
        {
            return true;
        }

        var signature = request.Headers["x-twilio-signature"].ToString();
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var publicUrl = current.PublicBaseUrl.TrimEnd('/')
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase);
        publicUrl += $"{request.Path}{request.QueryString}";
        return HasValidSignature(publicUrl, signature, current.AuthToken);
    }

    private static bool HasValidSignature(string payload, string signature, string authToken)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
        var expected = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(signature),
            Encoding.ASCII.GetBytes(expected));
    }
}

public static class TwimlFactory
{
    public static string CreateConversationRelay(CallSession call, TwilioOptions options)
    {
        var relayUrl = options.PublicBaseUrl.TrimEnd('/')
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase);
        relayUrl += "/twilio/conversation-relay";

        var response = new XElement("Response",
            new XElement("Connect",
                new XElement("ConversationRelay",
                    new XAttribute("url", relayUrl),
                    new XAttribute("language", TwilioRelayLanguage(call.Language)),
                    new XAttribute("welcomeGreeting", WelcomeGreeting(call.Language)),
                    new XElement("Parameter",
                        new XAttribute("name", "callId"),
                        new XAttribute("value", call.Id)))));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), response).ToString();
    }

    public static string WelcomeGreeting(string language) => language.Split('-')[0].ToLowerInvariant() switch
    {
        "ru" => "Здравствуйте. Я ИИ-ассистент и звоню по поручению пользователя. Разговор может быть расшифрован.",
        "uk" => "Вітаю. Я ШІ-помічник і телефоную за дорученням користувача. Розмову може бути розшифровано.",
        "pl" => "Dzień dobry. Jestem asystentem AI i dzwonię w imieniu użytkownika. Rozmowa może być transkrybowana.",
        "de" => "Guten Tag. Ich bin ein KI-Assistent und rufe im Namen eines Nutzers an. Das Gespräch kann transkribiert werden.",
        "es" => "Hola. Soy un asistente de inteligencia artificial y llamo en nombre de un usuario. La conversación puede ser transcrita.",
        "fr" => "Bonjour. Je suis un assistant IA et j'appelle au nom d'un utilisateur. La conversation peut être transcrite.",
        "it" => "Buongiorno. Sono un assistente IA e chiamo per conto di un utente. La conversazione potrebbe essere trascritta.",
        "cs" => "Dobrý den. Jsem asistent AI a volám jménem uživatele. Hovor může být přepsán.",
        _ => "Hello. I am an AI assistant calling on behalf of a user. This conversation may be transcribed."
    };

    private static string TwilioRelayLanguage(string language) =>
        string.IsNullOrWhiteSpace(language) ||
        language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "en-US"
            : language;
}

public static class TwilioStatusMapper
{
    public static CallStatus Map(string status) => status.ToLowerInvariant() switch
    {
        "queued" => CallStatus.Queued,
        "initiated" => CallStatus.Calling,
        "ringing" => CallStatus.Ringing,
        "in-progress" => CallStatus.InProgress,
        "completed" => CallStatus.Completed,
        "busy" => CallStatus.Busy,
        "no-answer" => CallStatus.NoAnswer,
        "canceled" => CallStatus.Canceled,
        "failed" => CallStatus.Failed,
        _ => CallStatus.Created
    };
}
