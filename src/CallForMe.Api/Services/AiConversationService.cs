using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CallForMe.Api.Models;
using CallForMe.Api.Options;
using Microsoft.Extensions.Options;

namespace CallForMe.Api.Services;

public sealed record AiTurn(
    string RemoteTranslation,
    string Reply,
    string ReplyTranslation,
    IReadOnlyList<ReplySuggestion> Suggestions);

public sealed record AiCallSummary(
    string Outcome,
    string KeyPoint,
    string NextStep,
    string Tone);

public sealed class AiConversationService
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly ILogger<AiConversationService> _logger;

    public AiConversationService(
        HttpClient httpClient,
        IOptionsMonitor<AiOptions> options,
        ILogger<AiConversationService> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<AiTurn> GenerateTurnAsync(CallSession call, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException("AI is not configured. Set AI:Enabled and AI:ApiKey before handling real calls.");
        }

        var transcript = string.Join("\n", call.Transcript.TakeLast(20).Select(entry =>
            $"{entry.Speaker}: {entry.Text}"));
        var callLanguage = IsAutoLanguage(call.Language)
            ? "the remote party's language, detected from their speech"
            : call.Language;
        var input = $$"""
            Goal for this phone call:
            {{call.Prompt}}

            Call language: {{callLanguage}}
            User language: {{call.UserLanguage}}

            Transcript:
            {{transcript}}

            Translate the latest remote message into the user's language: {{call.UserLanguage}}.

            Return only valid compact JSON:
            {"remoteTranslation":"latest remote message translated into the user's language","reply":"best short answer in the call language","replyTranslation":"reply translated into the user's language","suggestions":[{"text":"option in user language","spokenText":"same option in call language"}]}
            If the call language is auto, detect the remote party's language from the latest remote speech and reply in that same language.
            Keep the reply natural and under 35 words. Never invent facts, commitments, prices, or personal data.
            If uncertain, ask a clarifying question.
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = options.Model,
            instructions = $"You operate a disclosed AI phone assistant. Speak {callLanguage}; translate every remote message and assistant reply for a user who reads {call.UserLanguage}. Follow the user's goal and stay concise.",
            input,
            max_output_tokens = 300
        }), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"AI request failed with status {(int)response.StatusCode}: {json}");
            }

            return ParseTurn(json) ?? throw new InvalidOperationException("AI response did not contain a valid turn.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "AI request failed.");
            throw;
        }
    }

    private static AiTurn? ParseTurn(string responseJson)
    {
        using var response = JsonDocument.Parse(responseJson);
        foreach (var output in response.RootElement.GetProperty("output").EnumerateArray())
        {
            if (!output.TryGetProperty("content", out var content))
            {
                continue;
            }

            foreach (var item in content.EnumerateArray())
            {
                if (!item.TryGetProperty("text", out var textElement))
                {
                    continue;
                }

                var text = StripJsonFence(textElement.GetString());
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                using var turn = JsonDocument.Parse(text);
                var remoteTranslation = turn.RootElement.GetProperty("remoteTranslation").GetString();
                var reply = turn.RootElement.GetProperty("reply").GetString();
                var replyTranslation = turn.RootElement.GetProperty("replyTranslation").GetString();
                var suggestions = turn.RootElement.GetProperty("suggestions")
                    .EnumerateArray()
                    .Select(value => new ReplySuggestion(
                        value.GetProperty("text").GetString() ?? "",
                        value.GetProperty("spokenText").GetString() ?? ""))
                    .Where(value => !string.IsNullOrWhiteSpace(value.Text) && !string.IsNullOrWhiteSpace(value.SpokenText))
                    .Take(3)
                    .ToList();
                if (!string.IsNullOrWhiteSpace(reply))
                {
                    return new AiTurn(
                        remoteTranslation ?? "",
                        reply,
                        replyTranslation ?? "",
                        suggestions);
                }
            }
        }

        return null;
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!options.IsConfigured)
        {
            return text;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = options.Model,
            instructions = $"Translate the message into {targetLanguage}. Return only the translation.",
            input = text,
            max_output_tokens = 120
        }), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return response.IsSuccessStatusCode ? ExtractOutputText(json) ?? text : text;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Translation request failed; using original text.");
            return text;
        }
    }

    public async Task<AiCallSummary?> GenerateSummaryAsync(CallSession call, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!options.IsConfigured || call.Transcript.Count == 0)
        {
            return null;
        }

        var transcript = string.Join("\n", call.Transcript.Select(entry =>
            $"{entry.Speaker}: {entry.Text}" +
            (string.IsNullOrWhiteSpace(entry.Translation) ? "" : $" / {entry.Translation}")));
        var input = $$"""
            User goal:
            {{call.Prompt}}

            Call status: {{call.Status}}
            Duration seconds: {{call.DurationSeconds?.ToString() ?? "unknown"}}

            Transcript:
            {{transcript}}

            Return only compact JSON:
            {"outcome":"one short sentence in the user's language","keyPoint":"most important thing learned or said","nextStep":"clear next action for the user","tone":"one or two words"}

            Be factual. Do not invent results, dates, names, promises, or prices. If the transcript is too thin, say that briefly.
            """; 

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = options.Model,
            instructions = $"Summarize phone calls for a private user. Write in {call.UserLanguage}. Keep it short, useful, and calm.",
            input,
            max_output_tokens = 260
        }), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Summary request failed with status {Status}: {Body}", (int)response.StatusCode, json);
                return null;
            }

            return ParseSummary(json);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Summary request failed.");
            return null;
        }
    }

    private static string? ExtractOutputText(string responseJson)
    {
        using var response = JsonDocument.Parse(responseJson);
        foreach (var output in response.RootElement.GetProperty("output").EnumerateArray())
        {
            if (!output.TryGetProperty("content", out var content))
            {
                continue;
            }

            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var text))
                {
                    return text.GetString()?.Trim();
                }
            }
        }

        return null;
    }

    private static AiCallSummary? ParseSummary(string responseJson)
    {
        var text = StripJsonFence(ExtractOutputText(responseJson));
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        using var summary = JsonDocument.Parse(text);
        var root = summary.RootElement;
        return new AiCallSummary(
            root.TryGetProperty("outcome", out var outcome) ? outcome.GetString() ?? "" : "",
            root.TryGetProperty("keyPoint", out var keyPoint) ? keyPoint.GetString() ?? "" : "",
            root.TryGetProperty("nextStep", out var nextStep) ? nextStep.GetString() ?? "" : "",
            root.TryGetProperty("tone", out var tone) ? tone.GetString() ?? "" : "");
    }

    private static string? StripJsonFence(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            text = text.Trim('`').Trim();
            if (text.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                text = text[4..].Trim();
            }
        }

        return text;
    }

    private static bool IsAutoLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ||
        language.Equals("auto", StringComparison.OrdinalIgnoreCase);

}
