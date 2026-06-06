using CallForMe.Api.Models;

namespace CallForMe.Api.Services;

public sealed class TwilioCostRefreshService
{
    private readonly ICallRepository _repository;
    private readonly TwilioCallService _twilio;
    private readonly CallBillingService _billing;
    private readonly ILogger<TwilioCostRefreshService> _logger;

    public TwilioCostRefreshService(
        ICallRepository repository,
        TwilioCallService twilio,
        CallBillingService billing,
        ILogger<TwilioCostRefreshService> logger)
    {
        _repository = repository;
        _twilio = twilio;
        _billing = billing;
        _logger = logger;
    }

    public void Queue(Guid callId)
    {
        _ = Task.Run(() => RefreshWithRetriesAsync(callId, CancellationToken.None));
    }

    private async Task RefreshWithRetriesAsync(Guid callId, CancellationToken cancellationToken)
    {
        var delays = new[]
        {
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2)
        };

        foreach (var delay in delays)
        {
            await Task.Delay(delay, cancellationToken);
            if (await TryRefreshAsync(callId, cancellationToken))
            {
                return;
            }
        }
    }

    private async Task<bool> TryRefreshAsync(Guid callId, CancellationToken cancellationToken)
    {
        var call = await _repository.GetAsync(callId, cancellationToken);
        if (call is null ||
            string.IsNullOrWhiteSpace(call.TwilioCallSid) ||
            call.Billing?.TwilioVoiceActualCost is not null ||
            IsLiveStatus(call.Status))
        {
            return true;
        }

        try
        {
            var cost = await _twilio.GetCallCostAsync(call.TwilioCallSid, cancellationToken);
            if (cost is null)
            {
                await MarkErrorAsync(callId, "Twilio price is not available yet.", cancellationToken);
                return false;
            }

            await _repository.MutateAsync(callId, stored =>
            {
                stored.Billing = _billing.ApplyTwilioActualCost(stored, cost);
                stored.UpdatedAt = DateTimeOffset.UtcNow;
            }, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Failed to refresh Twilio call price for {CallId}.", callId);
            await MarkErrorAsync(callId, exception.Message, cancellationToken);
            return false;
        }
    }

    private async Task MarkErrorAsync(Guid callId, string error, CancellationToken cancellationToken)
    {
        await _repository.MutateAsync(callId, stored =>
        {
            stored.Billing = _billing.MarkTwilioActualCostError(stored, error);
            stored.UpdatedAt = DateTimeOffset.UtcNow;
        }, cancellationToken);
    }

    private static bool IsLiveStatus(CallStatus status) => status is
        CallStatus.Created or
        CallStatus.Queued or
        CallStatus.Calling or
        CallStatus.Ringing or
        CallStatus.InProgress;
}
