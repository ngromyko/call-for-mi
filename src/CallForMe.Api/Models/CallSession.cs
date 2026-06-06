namespace CallForMe.Api.Models;

public sealed class CallSession
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string? DisplayName { get; set; }
    public required string PhoneNumber { get; set; }
    public required string Prompt { get; set; }
    public string Language { get; set; } = "en-US";
    public string UserLanguage { get; set; } = "ru-RU";
    public bool AutoPilot { get; set; } = true;
    public bool RecordingConsentConfirmed { get; set; }
    public bool Hidden { get; set; }
    public string? TwilioCallSid { get; set; }
    public CallStatus Status { get; set; } = CallStatus.Created;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? RingingAt { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? DurationSeconds { get; set; }
    public CallBilling? Billing { get; set; }
    public CallUsageMetrics Usage { get; set; } = new();
    public string? Error { get; set; }
    public CallSummary? Summary { get; set; }
    public List<TranscriptEntry> Transcript { get; set; } = [];
    public List<ReplySuggestion> Suggestions { get; set; } = [];
}

public sealed class CallBilling
{
    public string Currency { get; set; } = "USD";
    public string DestinationCountry { get; set; } = "unknown";
    public string PricingTier { get; set; } = "premium";
    public long BilledSeconds { get; set; }
    public decimal BilledMinutes { get; set; }
    public decimal CustomerRatePerMinute { get; set; }
    public decimal EstimatedCustomerCost { get; set; }
    public decimal TwilioVoiceRatePerMinute { get; set; }
    public decimal TwilioConversationRelayRatePerMinute { get; set; }
    public decimal AiEstimatedRatePerMinute { get; set; }
    public decimal? TwilioVoiceActualCost { get; set; }
    public string? TwilioVoiceActualCostUnit { get; set; }
    public DateTimeOffset? TwilioVoiceActualCostFetchedAt { get; set; }
    public string? TwilioVoiceActualCostError { get; set; }
    public decimal EstimatedProviderCost { get; set; }
    public decimal EstimatedMargin { get; set; }
    public decimal FinalProviderCost { get; set; }
    public decimal FinalMargin { get; set; }
    public DateTimeOffset CalculatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CallUsageMetrics
{
    public int RemoteMessages { get; set; }
    public int AssistantMessages { get; set; }
    public int OperatorMessages { get; set; }
    public int AutopilotMessages { get; set; }
    public int SystemMessages { get; set; }
    public int Interruptions { get; set; }
    public int SuggestionsAvailable { get; set; }
    public int TranscriptCharacters { get; set; }
    public int TranslationCharacters { get; set; }
    public DateTimeOffset? FirstTranscriptAt { get; set; }
    public DateTimeOffset? LastTranscriptAt { get; set; }

    public static CallUsageMetrics From(CallSession call)
    {
        var transcript = call.Transcript;
        return new CallUsageMetrics
        {
            RemoteMessages = transcript.Count(entry => entry.Speaker == TranscriptSpeaker.Remote),
            AssistantMessages = transcript.Count(entry => entry.Speaker == TranscriptSpeaker.Assistant),
            OperatorMessages = transcript.Count(entry => entry.Source == "operator"),
            AutopilotMessages = transcript.Count(entry => entry.Source == "autopilot"),
            SystemMessages = transcript.Count(entry => entry.Speaker == TranscriptSpeaker.System),
            Interruptions = transcript.Count(entry => entry.Source == "conversation-relay" && entry.Speaker == TranscriptSpeaker.System),
            SuggestionsAvailable = call.Suggestions.Count,
            TranscriptCharacters = transcript.Sum(entry => entry.Text.Length),
            TranslationCharacters = transcript.Sum(entry => entry.Translation?.Length ?? 0),
            FirstTranscriptAt = transcript.Count == 0 ? null : transcript.Min(entry => entry.Timestamp),
            LastTranscriptAt = transcript.Count == 0 ? null : transcript.Max(entry => entry.Timestamp)
        };
    }
}

public sealed class CallSummary
{
    public string Outcome { get; set; } = "";
    public string KeyPoint { get; set; } = "";
    public string NextStep { get; set; } = "";
    public string Tone { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TranscriptEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TranscriptSpeaker Speaker { get; set; }
    public required string Text { get; set; }
    public string? Translation { get; set; }
    public required string Source { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record ReplySuggestion(string Text, string SpokenText);

public enum TranscriptSpeaker
{
    Remote,
    Assistant,
    System
}

public enum CallStatus
{
    Created,
    Queued,
    Calling,
    Ringing,
    InProgress,
    Completed,
    Failed,
    Busy,
    NoAnswer,
    Canceled
}
