using System.Globalization;
using CallForMe.Api.Models;
using Microsoft.Data.Sqlite;

namespace CallForMe.Api.Services;

public sealed class SqliteBillingRepository : IBillingRepository
{
    private readonly SqliteDatabase _database;

    public SqliteBillingRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<BalanceView> GetBalanceAsync(string clientId, CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        return await GetOrCreateBalanceAsync(connection, null, clientId, cancellationToken);
    }

    public async Task<IReadOnlyList<PromoCodeView>> ListPromoCodesAsync(CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                p.id,
                p.code,
                p.amount,
                p.max_redemptions,
                p.active,
                p.expires_at,
                p.created_at,
                COUNT(r.id) AS redemption_count
            FROM promo_codes p
            LEFT JOIN promo_redemptions r ON r.promo_code_id = p.id
            GROUP BY p.id
            ORDER BY p.created_at DESC
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var promoCodes = new List<PromoCodeView>();
        while (await reader.ReadAsync(cancellationToken))
        {
            promoCodes.Add(ReadPromoCodeView(reader));
        }

        return promoCodes;
    }

    public async Task<(BalanceView? Balance, string? Error)> DebitBalanceAsync(
        string clientId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var balance = await GetOrCreateBalanceAsync(connection, (SqliteTransaction)transaction, clientId, cancellationToken);
        if (balance.Balance < amount)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (null, $"Недостаточно баланса. Один звонок стоит {DecimalToString(amount)}.");
        }

        var newBalance = balance.Balance - amount;
        await UpdateBalanceAsync(connection, (SqliteTransaction)transaction, clientId, newBalance, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (new BalanceView(clientId, newBalance), null);
    }

    public async Task<BalanceView> CreditBalanceAsync(
        string clientId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var balance = await GetOrCreateBalanceAsync(connection, (SqliteTransaction)transaction, clientId, cancellationToken);
        var newBalance = balance.Balance + amount;
        await UpdateBalanceAsync(connection, (SqliteTransaction)transaction, clientId, newBalance, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BalanceView(clientId, newBalance);
    }

    public async Task<PromoCodeView> CreatePromoCodeAsync(
        string code,
        decimal amount,
        int? maxRedemptions,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var normalized = JsonBillingRepository.NormalizeCode(code);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO promo_codes (id, code, amount, max_redemptions, active, expires_at, created_at)
            VALUES ($id, $code, $amount, $max_redemptions, 1, $expires_at, $created_at)
            ON CONFLICT(code) DO UPDATE SET
                amount = excluded.amount,
                max_redemptions = excluded.max_redemptions,
                active = 1,
                expires_at = excluded.expires_at
            """;
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$code", normalized);
        command.Parameters.AddWithValue("$amount", DecimalToString(amount));
        command.Parameters.AddWithValue("$max_redemptions", maxRedemptions is null ? DBNull.Value : maxRedemptions);
        command.Parameters.AddWithValue("$expires_at", expiresAt is null ? DBNull.Value : expiresAt.Value.ToString("O"));
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetPromoCodeByCodeAsync(connection, normalized, cancellationToken))!;
    }

    public async Task<PromoCodeView?> SetPromoCodeActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE promo_codes SET active = $active WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$active", active ? 1 : 0);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows == 0 ? null : await GetPromoCodeByIdAsync(connection, id, cancellationToken);
    }

    public async Task<(BalanceView? Balance, string? Error)> RedeemPromoCodeAsync(
        string clientId,
        string code,
        CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var normalized = JsonBillingRepository.NormalizeCode(code);
        var promo = await GetPromoCodeByCodeAsync(connection, normalized, cancellationToken, (SqliteTransaction)transaction);
        if (promo is null || !promo.Active)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (null, "Промокод не найден или уже отключён.");
        }

        if (promo.ExpiresAt is not null && promo.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (null, "Срок действия промокода истёк.");
        }

        if (promo.MaxRedemptions is not null && promo.RedemptionCount >= promo.MaxRedemptions)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (null, "Лимит активаций промокода уже исчерпан.");
        }

        if (await HasRedemptionAsync(connection, (SqliteTransaction)transaction, promo.Id, clientId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return (null, "Этот промокод уже применён.");
        }

        var balance = await GetOrCreateBalanceAsync(connection, (SqliteTransaction)transaction, clientId, cancellationToken);
        var newBalance = balance.Balance + promo.Amount;
        await UpdateBalanceAsync(connection, (SqliteTransaction)transaction, clientId, newBalance, cancellationToken);

        await using var insertRedemption = connection.CreateCommand();
        insertRedemption.Transaction = (SqliteTransaction)transaction;
        insertRedemption.CommandText = """
            INSERT INTO promo_redemptions (id, promo_code_id, code, client_id, amount, created_at)
            VALUES ($id, $promo_code_id, $code, $client_id, $amount, $created_at)
            """;
        insertRedemption.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        insertRedemption.Parameters.AddWithValue("$promo_code_id", promo.Id.ToString());
        insertRedemption.Parameters.AddWithValue("$code", promo.Code);
        insertRedemption.Parameters.AddWithValue("$client_id", clientId);
        insertRedemption.Parameters.AddWithValue("$amount", DecimalToString(promo.Amount));
        insertRedemption.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
        await insertRedemption.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return (new BalanceView(clientId, newBalance), null);
    }

    private async Task<BalanceView> GetOrCreateBalanceAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string clientId,
        CancellationToken cancellationToken)
    {
        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT balance FROM client_balances WHERE client_id = $client_id";
        select.Parameters.AddWithValue("$client_id", clientId);
        var value = await select.ExecuteScalarAsync(cancellationToken);
        if (value is not null)
        {
            return new BalanceView(clientId, StringToDecimal(Convert.ToString(value) ?? "0"));
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO client_balances (client_id, balance, updated_at) VALUES ($client_id, $balance, $updated_at)";
        insert.Parameters.AddWithValue("$client_id", clientId);
        insert.Parameters.AddWithValue("$balance", "0");
        insert.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        await insert.ExecuteNonQueryAsync(cancellationToken);
        return new BalanceView(clientId, 0);
    }

    private static async Task UpdateBalanceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string clientId,
        decimal balance,
        CancellationToken cancellationToken)
    {
        await using var updateBalance = connection.CreateCommand();
        updateBalance.Transaction = transaction;
        updateBalance.CommandText = "UPDATE client_balances SET balance = $balance, updated_at = $updated_at WHERE client_id = $client_id";
        updateBalance.Parameters.AddWithValue("$balance", DecimalToString(balance));
        updateBalance.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        updateBalance.Parameters.AddWithValue("$client_id", clientId);
        await updateBalance.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> HasRedemptionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid promoCodeId,
        string clientId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM promo_redemptions WHERE promo_code_id = $promo_code_id AND client_id = $client_id LIMIT 1";
        command.Parameters.AddWithValue("$promo_code_id", promoCodeId.ToString());
        command.Parameters.AddWithValue("$client_id", clientId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private async Task<PromoCodeView?> GetPromoCodeByIdAsync(SqliteConnection connection, Guid id, CancellationToken cancellationToken)
    {
        await using var command = PromoCodeLookupCommand(connection);
        command.CommandText += " WHERE p.id = $id GROUP BY p.id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPromoCodeView(reader) : null;
    }

    private async Task<PromoCodeView?> GetPromoCodeByCodeAsync(
        SqliteConnection connection,
        string code,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = PromoCodeLookupCommand(connection);
        command.Transaction = transaction;
        command.CommandText += " WHERE p.code = $code GROUP BY p.id";
        command.Parameters.AddWithValue("$code", code);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPromoCodeView(reader) : null;
    }

    private static SqliteCommand PromoCodeLookupCommand(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                p.id,
                p.code,
                p.amount,
                p.max_redemptions,
                p.active,
                p.expires_at,
                p.created_at,
                COUNT(r.id) AS redemption_count
            FROM promo_codes p
            LEFT JOIN promo_redemptions r ON r.promo_code_id = p.id
            """;
        return command;
    }

    private static PromoCodeView ReadPromoCodeView(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
        reader.GetString(reader.GetOrdinal("code")),
        StringToDecimal(reader.GetString(reader.GetOrdinal("amount"))),
        reader.GetInt32(reader.GetOrdinal("redemption_count")),
        reader.IsDBNull(reader.GetOrdinal("max_redemptions")) ? null : reader.GetInt32(reader.GetOrdinal("max_redemptions")),
        reader.GetInt32(reader.GetOrdinal("active")) == 1,
        reader.IsDBNull(reader.GetOrdinal("expires_at")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("expires_at"))),
        DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))));

    private static string DecimalToString(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static decimal StringToDecimal(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
