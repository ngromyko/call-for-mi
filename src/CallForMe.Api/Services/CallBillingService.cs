using CallForMe.Api.Models;
using CallForMe.Api.Options;
using Microsoft.Extensions.Options;

namespace CallForMe.Api.Services;

public sealed class CallBillingService
{
    private readonly IOptionsMonitor<TwilioOptions> _twilio;

    public CallBillingService(IOptionsMonitor<TwilioOptions> twilio)
    {
        _twilio = twilio;
    }

    public CallBilling Calculate(CallSession call)
    {
        var destination = DestinationPricing.FromPhoneNumber(call.PhoneNumber, IsFromEea(_twilio.CurrentValue.FromNumber));
        var durationSeconds = call.DurationSeconds ?? EstimateDurationSeconds(call);
        var billedSeconds = durationSeconds <= 0 ? 0 : Math.Max(60, durationSeconds);
        var billedMinutes = Math.Round(billedSeconds / 60m, 2, MidpointRounding.AwayFromZero);
        var providerRate = destination.TwilioVoiceRatePerMinute +
            destination.TwilioConversationRelayRatePerMinute +
            destination.AiEstimatedRatePerMinute;
        var providerCost = Money(providerRate * billedMinutes);
        var customerCost = Money(destination.CustomerRatePerMinute * billedMinutes);

        return new CallBilling
        {
            Currency = "USD",
            DestinationCountry = destination.Country,
            PricingTier = destination.Tier,
            BilledSeconds = billedSeconds,
            BilledMinutes = billedMinutes,
            CustomerRatePerMinute = destination.CustomerRatePerMinute,
            EstimatedCustomerCost = customerCost,
            TwilioVoiceRatePerMinute = destination.TwilioVoiceRatePerMinute,
            TwilioConversationRelayRatePerMinute = destination.TwilioConversationRelayRatePerMinute,
            AiEstimatedRatePerMinute = destination.AiEstimatedRatePerMinute,
            EstimatedProviderCost = providerCost,
            EstimatedMargin = Money(customerCost - providerCost),
            FinalProviderCost = providerCost,
            FinalMargin = Money(customerCost - providerCost),
            CalculatedAt = DateTimeOffset.UtcNow
        };
    }

    public CallBilling ApplyTwilioActualCost(CallSession call, TwilioCallCost cost)
    {
        var billing = call.Billing ?? Calculate(call);
        var twilioVoiceCost = Money(Math.Abs(cost.Price));
        var relayAndAiCost = Money(
            (billing.TwilioConversationRelayRatePerMinute + billing.AiEstimatedRatePerMinute) *
            billing.BilledMinutes);
        var finalProviderCost = Money(twilioVoiceCost + relayAndAiCost);

        billing.TwilioVoiceActualCost = twilioVoiceCost;
        billing.TwilioVoiceActualCostUnit = string.IsNullOrWhiteSpace(cost.PriceUnit) ? billing.Currency : cost.PriceUnit;
        billing.TwilioVoiceActualCostFetchedAt = DateTimeOffset.UtcNow;
        billing.TwilioVoiceActualCostError = null;
        billing.FinalProviderCost = finalProviderCost;
        billing.FinalMargin = Money(billing.EstimatedCustomerCost - finalProviderCost);
        billing.CalculatedAt = DateTimeOffset.UtcNow;
        return billing;
    }

    public CallBilling MarkTwilioActualCostError(CallSession call, string error)
    {
        var billing = call.Billing ?? Calculate(call);
        billing.TwilioVoiceActualCostError = error;
        billing.TwilioVoiceActualCostFetchedAt = DateTimeOffset.UtcNow;
        billing.FinalProviderCost = billing.EstimatedProviderCost;
        billing.FinalMargin = billing.EstimatedMargin;
        billing.CalculatedAt = DateTimeOffset.UtcNow;
        return billing;
    }

    private static long EstimateDurationSeconds(CallSession call)
    {
        var start = call.AnsweredAt ?? call.CreatedAt;
        var end = call.CompletedAt ?? DateTimeOffset.UtcNow;
        return Math.Max(0, (long)Math.Ceiling((end - start).TotalSeconds));
    }

    private static bool IsFromEea(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return false;
        }

        var normalized = phoneNumber.Replace(" ", "", StringComparison.Ordinal);
        return normalized.StartsWith("+48", StringComparison.Ordinal) ||
            normalized.StartsWith("+49", StringComparison.Ordinal) ||
            normalized.StartsWith("+33", StringComparison.Ordinal) ||
            normalized.StartsWith("+34", StringComparison.Ordinal) ||
            normalized.StartsWith("+39", StringComparison.Ordinal) ||
            normalized.StartsWith("+31", StringComparison.Ordinal) ||
            normalized.StartsWith("+32", StringComparison.Ordinal) ||
            normalized.StartsWith("+420", StringComparison.Ordinal) ||
            normalized.StartsWith("+421", StringComparison.Ordinal) ||
            normalized.StartsWith("+370", StringComparison.Ordinal) ||
            normalized.StartsWith("+371", StringComparison.Ordinal) ||
            normalized.StartsWith("+372", StringComparison.Ordinal);
    }

    private static decimal Money(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private sealed record DestinationPricing(
        string Country,
        string Tier,
        decimal CustomerRatePerMinute,
        decimal TwilioVoiceRatePerMinute,
        decimal TwilioConversationRelayRatePerMinute,
        decimal AiEstimatedRatePerMinute)
    {
        private const decimal ConversationRelayRate = 0.07m;
        private const decimal AiEstimatedRate = 0.01m;

        public static DestinationPricing FromPhoneNumber(string phoneNumber, bool fromEea)
        {
            var normalized = phoneNumber.Replace(" ", "", StringComparison.Ordinal);
            if (normalized.StartsWith("+1", StringComparison.Ordinal))
            {
                return new("US/Canada", "standard", 0.29m, 0.014m, ConversationRelayRate, AiEstimatedRate);
            }

            if (normalized.StartsWith("+48", StringComparison.Ordinal))
            {
                var mobile = IsPolishMobile(normalized);
                var voiceRate = mobile
                    ? (fromEea ? 0.0715m : 0.2202m)
                    : (fromEea ? 0.0315m : 0.1114m);
                return new("Poland", mobile ? "premium" : "standard", mobile ? 0.49m : 0.29m, voiceRate, ConversationRelayRate, AiEstimatedRate);
            }

            return new("unknown", "premium", 0.49m, 0.22m, ConversationRelayRate, AiEstimatedRate);
        }

        private static bool IsPolishMobile(string normalized)
        {
            if (normalized.Length < 5)
            {
                return false;
            }

            var prefix = normalized[3..5];
            return prefix is "45" or "50" or "51" or "53" or "57" or "60" or "66" or "69" or
                "72" or "73" or "78" or "79" or "88";
        }
    }
}
