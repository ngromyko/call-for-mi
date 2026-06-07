using System.Globalization;
using CallForMe.Api.Models;
using Microsoft.Data.Sqlite;

namespace CallForMe.Api.Services;

public sealed class SqliteBillingRepository : IBillingRepository
{
    private static readonly TimeSpan TonConfirmationWindow = TimeSpan.FromSeconds(45);
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

    public async Task<IReadOnlyList<TonPaymentView>> ListTonPaymentsAsync(string? clientId, CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, client_id, external_id, comment, wallet_address, sender_address, asset_currency,
                   ton_amount, credits_amount, COALESCE(status, 'Confirmed') AS status, created_at, received_at
            FROM ton_payments
            """;
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            command.CommandText += " WHERE client_id = $client_id";
            command.Parameters.AddWithValue("$client_id", clientId);
        }
        command.CommandText += " ORDER BY created_at DESC";

        var payments = new List<TonPaymentView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            payments.Add(ReadTonPaymentView(reader));
        }

        return payments;
    }

    public async Task<(TonPaymentView Payment, BalanceView Balance, bool Created)> RecordTonPaymentAsync(
        string externalId,
        string clientId,
        string comment,
        string walletAddress,
        string? senderAddress,
        string currency,
        decimal tonAmount,
        decimal creditsAmount,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await GetTonPaymentByExternalIdAsync(connection, externalId, cancellationToken, (SqliteTransaction)transaction);
        if (existing is not null)
        {
            var shouldConfirm = existing.Status.Equals("Processing", StringComparison.OrdinalIgnoreCase) &&
                IsReadyToConfirm(existing.ReceivedAt);
            var currentBalance = shouldConfirm
                ? await ConfirmTonPaymentAsync(connection, (SqliteTransaction)transaction, existing, cancellationToken)
                : await GetOrCreateBalanceAsync(connection, (SqliteTransaction)transaction, existing.ClientId, cancellationToken);
            if (shouldConfirm)
            {
                await transaction.CommitAsync(cancellationToken);
                return (existing with { Status = "Confirmed" }, currentBalance, false);
            }

            await transaction.RollbackAsync(cancellationToken);
            return (existing, currentBalance, false);
        }

        var balance = await GetOrCreateBalanceAsync(connection, (SqliteTransaction)transaction, clientId, cancellationToken);
        var confirmed = IsReadyToConfirm(receivedAt);
        var newBalance = confirmed ? balance.Balance + creditsAmount : balance.Balance;
        if (confirmed)
        {
            await UpdateBalanceAsync(connection, (SqliteTransaction)transaction, clientId, newBalance, cancellationToken);
        }

        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        await using var insert = connection.CreateCommand();
        insert.Transaction = (SqliteTransaction)transaction;
        insert.CommandText = """
            INSERT INTO ton_payments (
                id, client_id, external_id, comment, wallet_address, sender_address, asset_currency,
                payment_link, ton_amount, credits_amount, status, created_at, received_at,
                submitted_at, confirmed_at, confirmed_by)
            VALUES (
                $id, $client_id, $external_id, $comment, $wallet_address, $sender_address, $asset_currency,
                $payment_link, $ton_amount, $credits_amount, $status, $created_at, $received_at,
                NULL, $confirmed_at, NULL)
            """;
        insert.Parameters.AddWithValue("$id", id.ToString());
        insert.Parameters.AddWithValue("$client_id", clientId);
        insert.Parameters.AddWithValue("$external_id", externalId);
        insert.Parameters.AddWithValue("$comment", comment);
        insert.Parameters.AddWithValue("$wallet_address", walletAddress);
        insert.Parameters.AddWithValue("$sender_address", string.IsNullOrWhiteSpace(senderAddress) ? DBNull.Value : senderAddress);
        insert.Parameters.AddWithValue("$asset_currency", JsonBillingRepository.NormalizeCurrency(currency));
        insert.Parameters.AddWithValue("$payment_link", "");
        insert.Parameters.AddWithValue("$ton_amount", DecimalToString(tonAmount));
        insert.Parameters.AddWithValue("$credits_amount", DecimalToString(creditsAmount));
        insert.Parameters.AddWithValue("$status", confirmed ? "Confirmed" : "Processing");
        insert.Parameters.AddWithValue("$created_at", createdAt.ToString("O"));
        insert.Parameters.AddWithValue("$received_at", receivedAt.ToString("O"));
        insert.Parameters.AddWithValue("$confirmed_at", confirmed ? createdAt.ToString("O") : DBNull.Value);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var payment = new TonPaymentView(
            id,
            clientId,
            externalId,
            comment,
            walletAddress,
            senderAddress,
            JsonBillingRepository.NormalizeCurrency(currency),
            tonAmount,
            creditsAmount,
            confirmed ? "Confirmed" : "Processing",
            createdAt,
            receivedAt);
        return (payment, new BalanceView(clientId, newBalance), true);
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

    private async Task<TonPaymentView?> GetTonPaymentByExternalIdAsync(
        SqliteConnection connection,
        string externalId,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, client_id, external_id, comment, wallet_address, sender_address, asset_currency,
                   ton_amount, credits_amount, COALESCE(status, 'Confirmed') AS status, created_at, received_at
            FROM ton_payments
            WHERE external_id = $external_id
            """;
        command.Parameters.AddWithValue("$external_id", externalId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTonPaymentView(reader) : null;
    }

    private static bool IsReadyToConfirm(DateTimeOffset receivedAt) =>
        DateTimeOffset.UtcNow - receivedAt >= TonConfirmationWindow;

    private static async Task<BalanceView> ConfirmTonPaymentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TonPaymentView payment,
        CancellationToken cancellationToken)
    {
        var balance = await GetOrCreateBalanceStaticAsync(connection, transaction, payment.ClientId, cancellationToken);
        var newBalance = balance.Balance + payment.CreditsAmount;
        await UpdateBalanceAsync(connection, transaction, payment.ClientId, newBalance, cancellationToken);

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE ton_payments
            SET status = 'Confirmed', confirmed_at = $confirmed_at
            WHERE external_id = $external_id AND COALESCE(status, 'Confirmed') = 'Processing'
            """;
        update.Parameters.AddWithValue("$confirmed_at", DateTimeOffset.UtcNow.ToString("O"));
        update.Parameters.AddWithValue("$external_id", payment.ExternalId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return new BalanceView(payment.ClientId, newBalance);
    }

    private static async Task<BalanceView> GetOrCreateBalanceStaticAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
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

    private static TonPaymentView ReadTonPaymentView(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
        reader.GetString(reader.GetOrdinal("client_id")),
        reader.GetString(reader.GetOrdinal("external_id")),
        reader.GetString(reader.GetOrdinal("comment")),
        reader.GetString(reader.GetOrdinal("wallet_address")),
        reader.IsDBNull(reader.GetOrdinal("sender_address")) ? null : reader.GetString(reader.GetOrdinal("sender_address")),
        reader.IsDBNull(reader.GetOrdinal("asset_currency")) ? "TON" : JsonBillingRepository.NormalizeCurrency(reader.GetString(reader.GetOrdinal("asset_currency"))),
        StringToDecimal(reader.GetString(reader.GetOrdinal("ton_amount"))),
        StringToDecimal(reader.GetString(reader.GetOrdinal("credits_amount"))),
        reader.IsDBNull(reader.GetOrdinal("status")) ? "Confirmed" : reader.GetString(reader.GetOrdinal("status")),
        DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
        reader.IsDBNull(reader.GetOrdinal("received_at")) ? DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))) : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("received_at"))));

    private static string DecimalToString(decimal value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
    private static decimal StringToDecimal(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
