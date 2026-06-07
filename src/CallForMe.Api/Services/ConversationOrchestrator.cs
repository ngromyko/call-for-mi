using CallForMe.Api.Hubs;
using CallForMe.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace CallForMe.Api.Services;

public sealed class ConversationOrchestrator
{
    private readonly ICallRepository _repository;
    private readonly AiConversationService _ai;
    private readonly ActiveRelayRegistry _relay;
    private readonly IHubContext<CallHub> _hub;
    private readonly CallBillingService _billing;

    public ConversationOrchestrator(
        ICallRepository repository,
        AiConversationService ai,
        ActiveRelayRegistry relay,
        IHubContext<CallHub> hub,
        CallBillingService billing)
    {
        _repository = repository;
        _ai = ai;
        _relay = relay;
        _hub = hub;
        _billing = billing;
    }

    public async Task<CallSession?> HandleRemotePromptAsync(
        Guid callId,
        string text,
        CancellationToken cancellationToken)
    {
        var remoteEntry = NewEntry(TranscriptSpeaker.Remote, text, "conversation-relay");
        var call = await _repository.MutateAsync(callId, stored =>
        {
            stored.Status = CallStatus.InProgress;
            stored.AnsweredAt ??= DateTimeOffset.UtcNow;
            stored.Transcript.Add(remoteEntry);
            stored.Usage = CallUsageMetrics.From(stored);
            stored.UpdatedAt = DateTimeOffset.UtcNow;
        }, cancellationToken);
        if (call is null)
        {
            return null;
        }

        await PublishTranscriptAsync(callId, remoteEntry, cancellationToken);
        var turn = await _ai.GenerateTurnAsync(call, cancellationToken);
        call = await _repository.MutateAsync(callId, stored =>
        {
            var entry = stored.Transcript.LastOrDefault(candidate => candidate.Id == remoteEntry.Id);
            if (entry is not null)
            {
                entry.Translation = turn.RemoteTranslation;
            }

            stored.Suggestions = turn.Suggestions.ToList();
            stored.Usage = CallUsageMetrics.From(stored);
            stored.UpdatedAt = DateTimeOffset.UtcNow;
        }, cancellationToken);

        if (call?.AutoPilot == true)
        {
            call = await SendAssistantMessageAsync(
                callId,
                turn.Reply,
                turn.ReplyTranslation,
                "autopilot",
                cancellationToken);
        }
        else if (call is not null)
        {
            await PublishCallAsync(call, cancellationToken);
        }

        return call;
    }

    public Task<CallSession?> SendOperatorMessageAsync(
        Guid callId,
        string text,
        string? spokenText,
        CancellationToken cancellationToken) =>
        SendTranslatedOperatorMessageAsync(callId, text, spokenText, cancellationToken);

    public async Task<CallSession?> SetAutoPilotAsync(
        Guid callId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var call = await _repository.MutateAsync(callId, stored =>
        {
            stored.AutoPilot = enabled;
            stored.UpdatedAt = DateTimeOffset.UtcNow;
        }, cancellationToken);
        if (call is not null)
        {
            await PublishCallAsync(call, cancellationToken);
        }

        return call;
    }

    private async Task GenerateAndPublishSummaryAsync(CallSession call, CancellationToken cancellationToken)
    {
        if (call.Summary is not null || call.Transcript.Count == 0)
        {
            return;
        }

        var summary = await _ai.GenerateSummaryAsync(call, cancellationToken);
        if (summary is null)
        {
            return;
        }

        var updated = await _repository.MutateAsync(call.Id, stored =>
        {
            stored.Summary = new CallSummary
            {
                Outcome = summary.Outcome,
                KeyPoint = summary.KeyPoint,
                NextStep = summary.NextStep,
                Tone = summary.Tone,
                CreatedAt = DateTimeOffset.UtcNow
            };
            stored.UpdatedAt = DateTimeOffset.UtcNow;
        }, cancellationToken);
        if (updated is not null)
        {
            await PublishCallAsync(updated, cancellationToken);
        }
    }

    public async Task<CallSession?> MarkConnectedAsync(Guid callId, CancellationToken cancellationToken)
    {
        TranscriptEntry? greetingEntry = null;
        var call = await _repository.MutateAsync(callId, stored =>
        {
            stored.Status = CallStatus.InProgress;
            stored.AnsweredAt ??= DateTimeOffset.UtcNow;
            if (!stored.Transcript.Any(entry => entry.Source == "welcome-greeting"))
            {
                greetingEntry = NewEntry(
                    TranscriptSpeaker.Assistant,
                    TwimlFactory.WelcomeGreeting(stored.Language),
                    "welcome-greeting");
                stored.Transcript.Add(greetingEntry);
            }

            stored.Usage = CallUsageMetrics.From(stored);
            stored.UpdatedAt = DateTimeOffset.UtcNow;
        }, cancellationToken);
        if (call is not null)
        {
            if (greetingEntry is not null)
            {
                await PublishTranscriptAsync(callId, greetingEntry, cancellationToken);
            }

            await PublishCallAsync(call, cancellationToken);
        }

        return call;
    }

    public async Task MarkInterruptedAsync(Guid callId, CancellationToken cancellationToken)
    {
        var entry = NewEntry(TranscriptSpeaker.System, "Собеседник перебил ответ ассистента.", "conversation-relay");
        var call = await _repository.MutateAsync(callId, stored =>
        {
            stored.Transcript.Add(entry);
            stored.Usage = CallUsageMetrics.From(stored);
            stored.UpdatedAt = DateTimeOffset.UtcNow;
        }, cancellationToken);
        if (call is not null)
        {
            await PublishTranscriptAsync(callId, entry, cancellationToken);
        }
    }

    public async Task<CallSession?> EndAsync(Guid callId, CancellationToken cancellationToken)
    {
        var call = await _repository.MutateAsync(callId, stored =>
        {
            stored.Status = CallStatus.Completed;
            stored.CompletedAt ??= DateTimeOffset.UtcNow;
            stored.DurationSeconds ??= EstimateDurationSeconds(stored);
            stored.Usage = CallUsageMetrics.From(stored);
            stored.Billing = _billing.Calculate(stored);
            stored.UpdatedAt = DateTimeOffset.UtcNow;
        }, cancellationToken);
        if (call is not null)
        {
            await PublishCallAsync(call, cancellationToken);
            await GenerateAndPublishSummaryAsync(call, cancellationToken);
        }

        return call;
    }

    public async Task<CallSession?> EnsureSummaryAsync(Guid callId, CancellationToken cancellationToken)
    {
        var call = await _repository.GetAsync(callId, cancellationToken);
        if (call is null)
        {
            return null;
        }

        await GenerateAndPublishSummaryAsync(call, cancellationToken);
        return await _repository.GetAsync(callId, cancellationToken);
    }

    private async Task<CallSession?> SendAssistantMessageAsync(
        Guid callId,
        string spokenText,
        string? translation,
        string source,
        CancellationToken cancellationToken)
    {
        await _relay.SendTextAsync(callId, spokenText, cancellationToken);
        var entry = NewEntry(TranscriptSpeaker.Assistant, spokenText, source);
        entry.Translation = translation;
        var call = await _repository.MutateAsync(callId, stored =>
        {
            stored.Transcript.Add(entry);
            stored.Suggestions.Clear();
            stored.Usage = CallUsageMetrics.From(stored);
            stored.UpdatedAt = DateTimeOffset.UtcNow;
        }, cancellationToken);
        if (call is not null)
        {
            await PublishTranscriptAsync(callId, entry, cancellationToken);
            await PublishCallAsync(call, cancellationToken);
        }

        return call;
    }

    private async Task<CallSession?> SendTranslatedOperatorMessageAsync(
        Guid callId,
        string text,
        string? spokenText,
        CancellationToken cancellationToken)
    {
        var call = await _repository.GetAsync(callId, cancellationToken);
        if (call is null)
        {
            return null;
        }

        spokenText ??= await _ai.TranslateAsync(text, call.Language, cancellationToken);
        return await SendAssistantMessageAsync(callId, spokenText, text, "operator", cancellationToken);
    }

    private Task PublishCallAsync(CallSession call, CancellationToken cancellationToken) =>
        _hub.Clients.Group(CallHub.GroupName(call.Id)).SendAsync("CallUpdated", call, cancellationToken);

    private Task PublishTranscriptAsync(Guid callId, TranscriptEntry entry, CancellationToken cancellationToken) =>
        _hub.Clients.Group(CallHub.GroupName(callId)).SendAsync("TranscriptAdded", entry, cancellationToken);

    private static TranscriptEntry NewEntry(TranscriptSpeaker speaker, string text, string source) => new()
    {
        Speaker = speaker,
        Text = text,
        Source = source,
        Timestamp = DateTimeOffset.UtcNow
    };

    private static long EstimateDurationSeconds(CallSession call)
    {
        var start = call.AnsweredAt ?? call.CreatedAt;
        var end = call.CompletedAt ?? DateTimeOffset.UtcNow;
        return Math.Max(0, (long)Math.Ceiling((end - start).TotalSeconds));
    }
}
