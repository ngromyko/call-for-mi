namespace CallForMe.Api.Models;

public sealed class UserAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string PasswordSalt { get; set; }
    public long? TelegramId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record UserAccountView(Guid Id, string Username);

public sealed record TelegramUserProfile(
    long Id,
    string? Username,
    string? FirstName,
    string? LastName,
    string? PhotoUrl);

public sealed record AdminUserStatsView(
    Guid Id,
    string Username,
    DateTimeOffset CreatedAt,
    decimal Balance,
    int TotalCalls,
    int CompletedCalls,
    int MissedCalls,
    long TotalDurationSeconds,
    DateTimeOffset? LastCallAt);
