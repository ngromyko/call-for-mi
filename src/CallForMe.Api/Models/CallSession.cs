namespace CallForMe.Api.Models;

public sealed class CallSession
{
    public Guid Id { get; set; }
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
    public long? DurationSeconds { get; set; }
    public string? Error { get; set; }
    public CallSummary? Summary { get; set; }
    public List<TranscriptEntry> Transcript { get; set; } = [];
    public List<ReplySuggestion> Suggestions { get; set; } = [];
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
