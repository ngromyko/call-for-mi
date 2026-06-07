using System.Collections.Concurrent;
using CallForMe.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CallForMe.Api.Services;

public sealed class CallMinuteBillingService(
    IServiceScopeFactory scopeFactory,
    ILogger<CallMinuteBillingService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, decimal> _chargedMinutesByCall = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnforceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to auto-process active call balances.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task EnforceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICallRepository>();
        var billing = scope.ServiceProvider.GetRequiredService<IBillingRepository>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ConversationOrchestrator>();
        var twilio = scope.ServiceProvider.GetRequiredService<TwilioCallService>();
        var costRefresh = scope.ServiceProvider.GetRequiredService<TwilioCostRefreshService>();

        var calls = await repository.ListAsync(cancellationToken);
        var activeCalls = calls.Where(call =>
            call.UserId is not null &&
            IsLiveStatus(call.Status))
            .ToList();

        var activeCallIds = activeCalls.Select(call => call.Id).ToHashSet();
        foreach (var callId in _chargedMinutesByCall.Keys)
        {
            if (!activeCallIds.Contains(callId))
            {
                _chargedMinutesByCall.TryRemove(callId, out _);
            }
        }

        foreach (var call in activeCalls)
        {
            await EnsureBalanceOrEndCallAsync(call, billing, repository, orchestrator, twilio, costRefresh, cancellationToken);
        }
    }

    private async Task EnsureBalanceOrEndCallAsync(
        CallSession call,
        IBillingRepository billing,
        ICallRepository repository,
        ConversationOrchestrator orchestrator,
        TwilioCallService twilio,
        TwilioCostRefreshService costRefresh,
        CancellationToken cancellationToken)
    {
        if (call.UserId is null)
        {
            return;
        }

        var chargedMinutes = _chargedMinutesByCall.GetOrAdd(call.Id, 1m);
        var requiredMinutes = EstimateRequiredMinutes(call);
        if (requiredMinutes <= chargedMinutes)
        {
            return;
        }

        var extraMinutes = requiredMinutes - chargedMinutes;
        var amountToDebit = Math.Round(extraMinutes * CallPricing.CreditsPerMinuteUsd, 2, MidpointRounding.AwayFromZero);
        if (amountToDebit <= 0)
        {
            return;
        }

        var clientId = BalanceClientId(call.UserId.Value);
        var (updatedBalance, error) = await billing.DebitBalanceAsync(clientId, amountToDebit, cancellationToken);
        if (updatedBalance is not null)
        {
            _chargedMinutesByCall[call.Id] = requiredMinutes;
            await repository.MutateAsync(call.Id, stored =>
            {
                stored.UpdatedAt = DateTimeOffset.UtcNow;
            }, cancellationToken);
            return;
        }

        logger.LogInformation("Auto-ending call {CallId} due to insufficient balance. Error: {Error}.", call.Id, error);

        var ended = await orchestrator.EndAsync(call.Id, cancellationToken);
        if (ended is null)
        {
            _chargedMinutesByCall.TryRemove(call.Id, out _);
            return;
        }

        _chargedMinutesByCall.TryRemove(ended.Id, out _);
        try
        {
            await repository.MutateAsync(ended.Id, stored =>
            {
                stored.Error = "Недостаточно кредитов. Звонок автоматически завершён.";
            }, cancellationToken);
            await twilio.EndCallAsync(ended, cancellationToken);
            costRefresh.Queue(ended.Id);
        }
        catch
        {
            // Ignore end-call transport issues while keeping local state coherent.
        }
    }

    private static decimal EstimateRequiredMinutes(CallSession call)
    {
        var start = call.AnsweredAt ?? call.CreatedAt;
        var elapsed = Math.Max(0, (DateTimeOffset.UtcNow - start).TotalSeconds);
        if (elapsed <= 0)
        {
            return 0m;
        }

        return Math.Ceiling((decimal)elapsed / 60m);
    }

    private static string BalanceClientId(Guid userId) => $"user-{userId:N}";

    private static bool IsLiveStatus(CallStatus status) => status is
        CallStatus.Created or
        CallStatus.Queued or
        CallStatus.Calling or
        CallStatus.Ringing or
        CallStatus.InProgress;
}
