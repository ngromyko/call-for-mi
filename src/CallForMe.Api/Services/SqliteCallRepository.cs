using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CallForMe.Api.Models;
using CallForMe.Api.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace CallForMe.Api.Services;

public sealed class SqliteCallRepository : ICallRepository
{
    private readonly SqliteDatabase _database;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public SqliteCallRepository(SqliteDatabase database, IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        _database = database;
        ImportLegacyJsonIfNeeded(options.Value, environment);
    }

    public async Task<CallSession> CreateAsync(CallSession call, CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await UpsertAsync(connection, call, cancellationToken);
        return call;
    }

    public async Task<CallSession?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM calls WHERE id = $id AND hidden = 0";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var call = ReadCall(reader);
        return IsRealCall(call) ? call : null;
    }

    public async Task<IReadOnlyList<CallSession>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM calls WHERE hidden = 0 ORDER BY created_at DESC";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var calls = new List<CallSession>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var call = ReadCall(reader);
            if (IsRealCall(call))
            {
                calls.Add(call);
            }
        }

        return calls;
    }

    public async Task<CallSession?> MutateAsync(Guid id, Action<CallSession> mutate, CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var call = await GetForUpdateAsync(connection, transaction, id, cancellationToken);
        if (call is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        mutate(call);
        await UpsertAsync(connection, call, cancellationToken, transaction);
        await transaction.CommitAsync(cancellationToken);
        return call;
    }

    private async Task<CallSession?> GetForUpdateAsync(
        SqliteConnection connection,
        IDbTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT * FROM calls WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCall(reader) : null;
    }

    private async Task UpsertAsync(
        SqliteConnection connection,
        CallSession call,
        CancellationToken cancellationToken,
        IDbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = (SqliteTransaction)transaction;
        }

        command.CommandText = """
            INSERT INTO calls (
                id, user_id, display_name, phone_number, prompt, language, user_language, auto_pilot,
                recording_consent_confirmed, hidden, twilio_call_sid, status, created_at, updated_at,
                ringing_at, answered_at, completed_at, duration_seconds, billed_seconds,
                estimated_customer_cost, estimated_provider_cost, estimated_margin,
                twilio_voice_actual_cost, final_provider_cost, final_margin, pricing_tier,
                billing_json, usage_json, error, summary_json, transcript_json, suggestions_json
            ) VALUES (
                $id, $user_id, $display_name, $phone_number, $prompt, $language, $user_language, $auto_pilot,
                $recording_consent_confirmed, $hidden, $twilio_call_sid, $status, $created_at, $updated_at,
                $ringing_at, $answered_at, $completed_at, $duration_seconds, $billed_seconds,
                $estimated_customer_cost, $estimated_provider_cost, $estimated_margin,
                $twilio_voice_actual_cost, $final_provider_cost, $final_margin, $pricing_tier,
                $billing_json, $usage_json, $error, $summary_json, $transcript_json, $suggestions_json
            )
            ON CONFLICT(id) DO UPDATE SET
                user_id = excluded.user_id,
                display_name = excluded.display_name,
                phone_number = excluded.phone_number,
                prompt = excluded.prompt,
                language = excluded.language,
                user_language = excluded.user_language,
                auto_pilot = excluded.auto_pilot,
                recording_consent_confirmed = excluded.recording_consent_confirmed,
                hidden = excluded.hidden,
                twilio_call_sid = excluded.twilio_call_sid,
                status = excluded.status,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at,
                ringing_at = excluded.ringing_at,
                answered_at = excluded.answered_at,
                completed_at = excluded.completed_at,
                duration_seconds = excluded.duration_seconds,
                billed_seconds = excluded.billed_seconds,
                estimated_customer_cost = excluded.estimated_customer_cost,
                estimated_provider_cost = excluded.estimated_provider_cost,
                estimated_margin = excluded.estimated_margin,
                twilio_voice_actual_cost = excluded.twilio_voice_actual_cost,
                final_provider_cost = excluded.final_provider_cost,
                final_margin = excluded.final_margin,
                pricing_tier = excluded.pricing_tier,
                billing_json = excluded.billing_json,
                usage_json = excluded.usage_json,
                error = excluded.error,
                summary_json = excluded.summary_json,
                transcript_json = excluded.transcript_json,
                suggestions_json = excluded.suggestions_json;
            """;
        command.Parameters.AddWithValue("$id", call.Id.ToString());
        command.Parameters.AddWithValue("$user_id", call.UserId is null ? DBNull.Value : call.UserId.Value.ToString());
        command.Parameters.AddWithValue("$display_name", DbValue(call.DisplayName));
        command.Parameters.AddWithValue("$phone_number", call.PhoneNumber);
        command.Parameters.AddWithValue("$prompt", call.Prompt);
        command.Parameters.AddWithValue("$language", call.Language);
        command.Parameters.AddWithValue("$user_language", call.UserLanguage);
        command.Parameters.AddWithValue("$auto_pilot", call.AutoPilot ? 1 : 0);
        command.Parameters.AddWithValue("$recording_consent_confirmed", call.RecordingConsentConfirmed ? 1 : 0);
        command.Parameters.AddWithValue("$hidden", call.Hidden ? 1 : 0);
        command.Parameters.AddWithValue("$twilio_call_sid", DbValue(call.TwilioCallSid));
        command.Parameters.AddWithValue("$status", call.Status.ToString());
        command.Parameters.AddWithValue("$created_at", call.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated_at", call.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$ringing_at", call.RingingAt is null ? DBNull.Value : call.RingingAt.Value.ToString("O"));
        command.Parameters.AddWithValue("$answered_at", call.AnsweredAt is null ? DBNull.Value : call.AnsweredAt.Value.ToString("O"));
        command.Parameters.AddWithValue("$completed_at", call.CompletedAt is null ? DBNull.Value : call.CompletedAt.Value.ToString("O"));
        command.Parameters.AddWithValue("$duration_seconds", call.DurationSeconds is null ? DBNull.Value : call.DurationSeconds);
        command.Parameters.AddWithValue("$billed_seconds", call.Billing is null ? DBNull.Value : call.Billing.BilledSeconds);
        command.Parameters.AddWithValue("$estimated_customer_cost", call.Billing is null ? DBNull.Value : MoneyString(call.Billing.EstimatedCustomerCost));
        command.Parameters.AddWithValue("$estimated_provider_cost", call.Billing is null ? DBNull.Value : MoneyString(call.Billing.EstimatedProviderCost));
        command.Parameters.AddWithValue("$estimated_margin", call.Billing is null ? DBNull.Value : MoneyString(call.Billing.EstimatedMargin));
        command.Parameters.AddWithValue("$twilio_voice_actual_cost", call.Billing?.TwilioVoiceActualCost is null ? DBNull.Value : MoneyString(call.Billing.TwilioVoiceActualCost.Value));
        command.Parameters.AddWithValue("$final_provider_cost", call.Billing is null ? DBNull.Value : MoneyString(call.Billing.FinalProviderCost));
        command.Parameters.AddWithValue("$final_margin", call.Billing is null ? DBNull.Value : MoneyString(call.Billing.FinalMargin));
        command.Parameters.AddWithValue("$pricing_tier", call.Billing is null ? DBNull.Value : call.Billing.PricingTier);
        command.Parameters.AddWithValue("$billing_json", call.Billing is null ? DBNull.Value : JsonSerializer.Serialize(call.Billing, _jsonOptions));
        command.Parameters.AddWithValue("$usage_json", JsonSerializer.Serialize(call.Usage, _jsonOptions));
        command.Parameters.AddWithValue("$error", DbValue(call.Error));
        command.Parameters.AddWithValue("$summary_json", call.Summary is null ? DBNull.Value : JsonSerializer.Serialize(call.Summary, _jsonOptions));
        command.Parameters.AddWithValue("$transcript_json", JsonSerializer.Serialize(call.Transcript, _jsonOptions));
        command.Parameters.AddWithValue("$suggestions_json", JsonSerializer.Serialize(call.Suggestions, _jsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private CallSession ReadCall(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
        UserId = ReadNullableGuid(reader, "user_id"),
        DisplayName = ReadNullableString(reader, "display_name"),
        PhoneNumber = reader.GetString(reader.GetOrdinal("phone_number")),
        Prompt = reader.GetString(reader.GetOrdinal("prompt")),
        Language = reader.GetString(reader.GetOrdinal("language")),
        UserLanguage = reader.GetString(reader.GetOrdinal("user_language")),
        AutoPilot = reader.GetInt32(reader.GetOrdinal("auto_pilot")) == 1,
        RecordingConsentConfirmed = reader.GetInt32(reader.GetOrdinal("recording_consent_confirmed")) == 1,
        Hidden = reader.GetInt32(reader.GetOrdinal("hidden")) == 1,
        TwilioCallSid = ReadNullableString(reader, "twilio_call_sid"),
        Status = Enum.TryParse<CallStatus>(reader.GetString(reader.GetOrdinal("status")), out var status) ? status : CallStatus.Created,
        CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
        UpdatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at"))),
        RingingAt = ReadNullableDateTimeOffset(reader, "ringing_at"),
        AnsweredAt = ReadNullableDateTimeOffset(reader, "answered_at"),
        CompletedAt = ReadNullableDateTimeOffset(reader, "completed_at"),
        DurationSeconds = reader.IsDBNull(reader.GetOrdinal("duration_seconds")) ? null : reader.GetInt64(reader.GetOrdinal("duration_seconds")),
        Billing = ReadJson<CallBilling>(reader, "billing_json"),
        Usage = ReadJson<CallUsageMetrics>(reader, "usage_json") ?? new(),
        Error = ReadNullableString(reader, "error"),
        Summary = ReadJson<CallSummary>(reader, "summary_json"),
        Transcript = ReadJson<List<TranscriptEntry>>(reader, "transcript_json") ?? [],
        Suggestions = ReadJson<List<ReplySuggestion>>(reader, "suggestions_json") ?? []
    };

    private T? ReadJson<T>(SqliteDataReader reader, string name)
    {
        var value = ReadNullableString(reader, name);
        return string.IsNullOrWhiteSpace(value) ? default : JsonSerializer.Deserialize<T>(value, _jsonOptions);
    }

    private static string? ReadNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(SqliteDataReader reader, string name)
    {
        var value = ReadNullableString(reader, name);
        return string.IsNullOrWhiteSpace(value) ? null : DateTimeOffset.Parse(value);
    }

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static Guid? ReadNullableGuid(SqliteDataReader reader, string name)
    {
        var value = ReadNullableString(reader, name);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static string MoneyString(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static bool IsRealCall(CallSession call) =>
        !call.Hidden &&
        (string.IsNullOrWhiteSpace(call.TwilioCallSid) ||
        !call.TwilioCallSid.StartsWith("SIM", StringComparison.OrdinalIgnoreCase));

    private void ImportLegacyJsonIfNeeded(StorageOptions options, IHostEnvironment environment)
    {
        using var connection = _database.OpenConnection();
        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM calls";
        var count = Convert.ToInt32(countCommand.ExecuteScalar());
        if (count > 0)
        {
            return;
        }

        var legacyPath = Path.IsPathRooted(options.LegacyCallsJsonPath)
            ? options.LegacyCallsJsonPath
            : Path.Combine(environment.ContentRootPath, options.LegacyCallsJsonPath);
        if (!File.Exists(legacyPath))
        {
            return;
        }

        using var stream = File.OpenRead(legacyPath);
        var calls = JsonSerializer.Deserialize<List<CallSession>>(stream, _jsonOptions) ?? [];
        foreach (var call in calls)
        {
            UpsertAsync(connection, call, CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
