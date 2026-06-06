using System.Security.Cryptography;
using CallForMe.Api.Models;
using Microsoft.Data.Sqlite;

namespace CallForMe.Api.Services;

public interface IUserRepository
{
    Task<UserAccountView?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<(UserAccountView? User, string? Error)> RegisterAsync(string username, string password, CancellationToken cancellationToken);
    Task<UserAccountView?> ValidateLoginAsync(string username, string password, CancellationToken cancellationToken);
}

public sealed class SqliteUserRepository(SqliteDatabase database) : IUserRepository
{
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

    public async Task<(UserAccountView? User, string? Error)> RegisterAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeUsername(username);
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

    private static byte[] HashPassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 120_000, HashAlgorithmName.SHA256, 32);
}
