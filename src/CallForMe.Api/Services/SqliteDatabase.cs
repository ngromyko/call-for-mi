using CallForMe.Api.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace CallForMe.Api.Services;

public sealed class SqliteDatabase
{
    private readonly string _connectionString;

    public SqliteDatabase(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        var databasePath = string.IsNullOrWhiteSpace(options.Value.DatabasePath)
            ? "data/callforme.db"
            : options.Value.DatabasePath;
        var path = Path.IsPathRooted(databasePath)
            ? databasePath
            : Path.Combine(environment.ContentRootPath, databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        Initialize();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS calls (
                id TEXT PRIMARY KEY,
                user_id TEXT NULL,
                display_name TEXT NULL,
                phone_number TEXT NOT NULL,
                prompt TEXT NOT NULL,
                language TEXT NOT NULL,
                user_language TEXT NOT NULL,
                auto_pilot INTEGER NOT NULL,
                recording_consent_confirmed INTEGER NOT NULL,
                hidden INTEGER NOT NULL,
                twilio_call_sid TEXT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                ringing_at TEXT NULL,
                answered_at TEXT NULL,
                completed_at TEXT NULL,
                duration_seconds INTEGER NULL,
                billed_seconds INTEGER NULL,
                estimated_customer_cost TEXT NULL,
                estimated_provider_cost TEXT NULL,
                estimated_margin TEXT NULL,
                twilio_voice_actual_cost TEXT NULL,
                final_provider_cost TEXT NULL,
                final_margin TEXT NULL,
                pricing_tier TEXT NULL,
                billing_json TEXT NULL,
                usage_json TEXT NULL,
                error TEXT NULL,
                summary_json TEXT NULL,
                transcript_json TEXT NOT NULL,
                suggestions_json TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS users (
                id TEXT PRIMARY KEY,
                username TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                password_salt TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS promo_codes (
                id TEXT PRIMARY KEY,
                code TEXT NOT NULL UNIQUE,
                amount TEXT NOT NULL,
                max_redemptions INTEGER NULL,
                active INTEGER NOT NULL,
                expires_at TEXT NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS client_balances (
                client_id TEXT PRIMARY KEY,
                balance TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS promo_redemptions (
                id TEXT PRIMARY KEY,
                promo_code_id TEXT NOT NULL,
                code TEXT NOT NULL,
                client_id TEXT NOT NULL,
                amount TEXT NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(promo_code_id, client_id),
                FOREIGN KEY(promo_code_id) REFERENCES promo_codes(id)
            );
            """;
        command.ExecuteNonQuery();
        AddColumnIfMissing(connection, "calls", "user_id", "TEXT NULL");
        AddColumnIfMissing(connection, "calls", "ringing_at", "TEXT NULL");
        AddColumnIfMissing(connection, "calls", "answered_at", "TEXT NULL");
        AddColumnIfMissing(connection, "calls", "completed_at", "TEXT NULL");
        AddColumnIfMissing(connection, "calls", "billed_seconds", "INTEGER NULL");
        AddColumnIfMissing(connection, "calls", "estimated_customer_cost", "TEXT NULL");
        AddColumnIfMissing(connection, "calls", "estimated_provider_cost", "TEXT NULL");
        AddColumnIfMissing(connection, "calls", "estimated_margin", "TEXT NULL");
        AddColumnIfMissing(connection, "calls", "twilio_voice_actual_cost", "TEXT NULL");
        AddColumnIfMissing(connection, "calls", "final_provider_cost", "TEXT NULL");
        AddColumnIfMissing(connection, "calls", "final_margin", "TEXT NULL");
        AddColumnIfMissing(connection, "calls", "pricing_tier", "TEXT NULL");
        AddColumnIfMissing(connection, "calls", "billing_json", "TEXT NULL");
        AddColumnIfMissing(connection, "calls", "usage_json", "TEXT NULL");
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string table, string column, string type)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table})";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
        alter.ExecuteNonQuery();
    }
}
