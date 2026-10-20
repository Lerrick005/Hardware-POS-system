using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Customers;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Customers;

public sealed class CustomerAccountService : ICustomerAccountService
{
    private static readonly HashSet<string> AllowedPaymentMethods =
        new(StringComparer.Ordinal)
        {
            "CASH", "MPESA", "CARD", "BANK", "OTHER",
        };

    private readonly DbConnectionFactory _factory;

    public CustomerAccountService(DbConnectionFactory factory) => _factory = factory;

    public async Task<RecordPaymentResult> RecordPaymentAsync(
        RecordPaymentRequest request,
        CancellationToken ct = default)
    {
        ValidatePayment(request);

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            bool customerExists = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(1) FROM customers WHERE id = $id AND is_active = 1",
                new { id = request.CustomerId }, tx, cancellationToken: ct)) > 0;

            if (!customerExists)
            {
                throw new CustomerAccountException(
                    $"Customer {request.CustomerId} does not exist or is inactive.");
            }

            long entryId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
                INSERT INTO customer_ledger(customer_id, entry_type, amount_cents,
                                            cash_session_id, method, reference, notes,
                                            user_id, business_date)
                VALUES ($CustomerId, 'PAYMENT', $AmountCents,
                        $CashSessionId, $Method, $Reference, $Notes,
                        $UserId, $BusinessDate)
                RETURNING id",
                new
                {
                    request.CustomerId,
                    AmountCents = -request.AmountCents,   // payments reduce what is owed
                    request.CashSessionId,
                    request.Method,
                    request.Reference,
                    request.Notes,
                    request.UserId,
                    request.BusinessDate,
                },
                tx, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO audit_log(user_id, action, entity_type, entity_id, new_value)
                VALUES ($UserId, 'CUSTOMER_PAYMENT', 'customer_ledger', $EntryId, $Amount)",
                new
                {
                    request.UserId,
                    EntryId = (int)entryId,
                    Amount = request.AmountCents.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                tx, cancellationToken: ct));

            long newBalance = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COALESCE(SUM(amount_cents), 0) FROM customer_ledger WHERE customer_id = $customerId",
                new { customerId = request.CustomerId }, tx, cancellationToken: ct));

            tx.Commit();
            return new RecordPaymentResult((int)entryId, newBalance);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<SetOpeningBalanceResult> SetOpeningBalanceAsync(
        SetOpeningBalanceRequest request,
        CancellationToken ct = default)
    {
        if (request.AmountCents <= 0)
        {
            throw new CustomerAccountException(
                "Opening balance must be greater than zero. Use payments to reduce a debt.");
        }

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            bool customerExists = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(1) FROM customers WHERE id = $id AND is_active = 1",
                new { id = request.CustomerId }, tx, cancellationToken: ct)) > 0;

            if (!customerExists)
            {
                throw new CustomerAccountException(
                    $"Customer {request.CustomerId} does not exist or is inactive.");
            }

            bool alreadyHasOpening = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(1) FROM customer_ledger WHERE customer_id = $id AND entry_type = 'OPENING'",
                new { id = request.CustomerId }, tx, cancellationToken: ct)) > 0;

            if (alreadyHasOpening)
            {
                throw new CustomerAccountException(
                    "This customer already has an opening balance. Use a payment or adjustment to change it.");
            }

            long entryId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
                INSERT INTO customer_ledger(customer_id, entry_type, amount_cents,
                                            notes, user_id, business_date)
                VALUES ($CustomerId, 'OPENING', $AmountCents,
                        $Notes, $UserId, $BusinessDate)
                RETURNING id",
                new
                {
                    request.CustomerId,
                    request.AmountCents,
                    request.Notes,
                    request.UserId,
                    request.BusinessDate,
                },
                tx, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO audit_log(user_id, action, entity_type, entity_id, new_value)
                VALUES ($UserId, 'CUSTOMER_OPENING_BALANCE', 'customer_ledger', $EntryId, $Amount)",
                new
                {
                    request.UserId,
                    EntryId = (int)entryId,
                    Amount = request.AmountCents.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                tx, cancellationToken: ct));

            long newBalance = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COALESCE(SUM(amount_cents), 0) FROM customer_ledger WHERE customer_id = $customerId",
                new { customerId = request.CustomerId }, tx, cancellationToken: ct));

            tx.Commit();
            return new SetOpeningBalanceResult((int)entryId, newBalance);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void ValidatePayment(RecordPaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AmountCents <= 0)
        {
            throw new CustomerAccountException("Payment amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.Method) ||
            !AllowedPaymentMethods.Contains(request.Method))
        {
            throw new CustomerAccountException(
                $"Payment method must be one of: {string.Join(", ", AllowedPaymentMethods)}.");
        }

        if (string.IsNullOrWhiteSpace(request.BusinessDate) ||
            !System.Text.RegularExpressions.Regex.IsMatch(request.BusinessDate, "^[0-9]{4}-[0-9]{2}-[0-9]{2}$"))
        {
            throw new CustomerAccountException("Date must be YYYY-MM-DD.");
        }
    }
}
