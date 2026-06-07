using System.Security.Cryptography;
using CallForMe.Api.Models;
using CallForMe.Api.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace CallForMe.Api.Services;

public interface IUserRepository
{
    Task<UserAccountView?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminUserStatsView>> ListWithStatsAsync(CancellationToken cancellationToken);
    Task<(UserAccountView? User, string? Error)> RegisterAsync(string username, string password, CancellationToken cancellationToken);
    Task<UserAccountView?> ValidateLoginAsync(string username, string password, CancellationToken cancellationToken);
}

public sealed class SqliteUserRepository(SqliteDatabase database, IOptionsMonitor<AdminOptions> adminOptions) : IUserRepository
{
    private const string AdminUsername = "admin";

    public async Task<UserAccountView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, username FROM users WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new UserAccountView(Guid.Parse(reader.GetString(0)), reader.GetString(1))
            : null;
    }

    public async Task<IReadOnlyList<AdminUserStatsView>> ListWithStatsAsync(CancellationToken cancellationToken)
    {
        EnsureAdminUser();
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                u.id,
                u.username,
                u.created_at,
                COALESCE(b.balance, '0') AS balance,
                COUNT(c.id) AS total_calls,
                SUM(CASE WHEN c.status = 'Completed' THEN 1 ELSE 0 END) AS completed_calls,
                SUM(CASE WHEN c.status IN ('NoAnswer', 'Busy', 'Canceled', 'Failed') THEN 1 ELSE 0 END) AS missed_calls,
                COALESCE(SUM(COALESCE(c.duration_seconds, 0)), 0) AS total_duration_seconds,
                MAX(c.created_at) AS last_call_at
            FROM users u
            LEFT JOIN calls c ON c.user_id = u.id
            LEFT JOIN client_balances b ON b.client_id = 'user-' || lower(replace(u.id, '-', ''))
            GROUP BY u.id, u.username, u.created_at, b.balance
            ORDER BY last_call_at IS NULL, last_call_at DESC, u.created_at DESC
            """;

        var users = new List<AdminUserStatsView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var lastCall = reader.IsDBNull(8) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(8));
            users.Add(new AdminUserStatsView(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                decimal.TryParse(reader.GetString(3), out var balance) ? balance : 0m,
                Convert.ToInt32(reader.GetInt64(4)),
                Convert.ToInt32(reader.GetInt64(5)),
                Convert.ToInt32(reader.GetInt64(6)),
                reader.GetInt64(7),
                lastCall));
        }

        return users;
    }

    public async Task<(UserAccountView? User, string? Error)> RegisterAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        EnsureAdminUser();
        var normalized = NormalizeUsername(username);
        if (IsAdminUsername(normalized))
        {
            return (null, "Username admin зарезервирован.");
        }

        if (!IsValidUsername(normalized))
        {
            return (null, "Username должен быть 3-24 символа: буквы, цифры, дефис или подчёркивание.");
        }

        if (password.Length < 6)
        {
            return (null, "Пароль должен быть минимум 6 символов.");
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(password, salt);
        var id = Guid.NewGuid();

        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (id, username, password_hash, password_salt, created_at)
            VALUES ($id, $username, $password_hash, $password_salt, $created_at)
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$username", normalized);
        command.Parameters.AddWithValue("$password_hash", Convert.ToBase64String(hash));
        command.Parameters.AddWithValue("$password_salt", Convert.ToBase64String(salt));
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            return (null, "Такой username уже занят.");
        }

        return (new UserAccountView(id, normalized), null);
    }

    public async Task<UserAccountView?> ValidateLoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        EnsureAdminUser();
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, username, password_hash, password_salt FROM users WHERE username = $username";
        command.Parameters.AddWithValue("$username", NormalizeUsername(username));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var expected = Convert.FromBase64String(reader.GetString(2));
        var salt = Convert.FromBase64String(reader.GetString(3));
        var actual = HashPassword(password, salt);
        return CryptographicOperations.FixedTimeEquals(actual, expected)
            ? new UserAccountView(Guid.Parse(reader.GetString(0)), reader.GetString(1))
            : null;
    }

    public static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();

    private static bool IsValidUsername(string username) =>
        username.Length is >= 3 and <= 24 &&
        username.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static bool IsAdminUsername(string username) =>
        string.Equals(username, AdminUsername, StringComparison.Ordinal);

    private void EnsureAdminUser()
    {
        var password = adminOptions.CurrentValue.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(password, salt);

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (id, username, password_hash, password_salt, created_at)
            VALUES ($id, $username, $password_hash, $password_salt, $created_at)
            ON CONFLICT(username) DO UPDATE SET
                password_hash = excluded.password_hash,
                password_salt = excluded.password_salt
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$username", AdminUsername);
        command.Parameters.AddWithValue("$password_hash", Convert.ToBase64String(hash));
        command.Parameters.AddWithValue("$password_salt", Convert.ToBase64String(salt));
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static byte[] HashPassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 120_000, HashAlgorithmName.SHA256, 32);
}
