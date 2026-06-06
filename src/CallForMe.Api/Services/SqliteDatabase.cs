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
                duration_seconds INTEGER NULL,
                error TEXT NULL,
                summary_json TEXT NULL,
                transcript_json TEXT NOT NULL,
                suggestions_json TEXT NOT NULL
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
    }
}
