using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Customers;
using Pos.Domain.Customers;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Customers;

public sealed class CustomerRepository : ICustomerRepository
{
    private readonly DbConnectionFactory _factory;

    public CustomerRepository(DbConnectionFactory factory) => _factory = factory;

    private const string BalanceColumn =
        "COALESCE((SELECT SUM(amount_cents) FROM customer_ledger WHERE customer_id = c.id), 0)";

    private const string ListSelect = @"
        SELECT c.id                 AS Id,
               c.name               AS Name,
               c.phone              AS Phone,
               c.price_tier_id      AS PriceTierId,
               pt.name              AS PriceTierName,
               c.credit_limit_cents AS CreditLimitCents,
               " + BalanceColumn + @" AS BalanceCents,
               c.is_active          AS IsActive
          FROM customers c
          LEFT JOIN price_tiers pt ON pt.id = c.price_tier_id";

    private const string CustomerSelect = @"
        SELECT id AS Id, uuid AS Uuid, name AS Name, phone AS Phone, email AS Email,
               kra_pin AS KraPin, price_tier_id AS PriceTierId, credit_limit_cents AS CreditLimitCents,
               notes AS Notes, is_active AS IsActive, import_batch_id AS ImportBatchId,
               created_at AS CreatedAt
          FROM customers";

    public async Task<IReadOnlyList<CustomerListItem>> ListAsync(
        bool includeInactive = false,
        bool onlyDebtors = false,
        CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!includeInactive)
        {
            where.Add("c.is_active = 1");
        }
        if (onlyDebtors)
        {
            where.Add(BalanceColumn + " > 0");
        }

        string sql = ListSelect;
        if (where.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", where);
        }
        sql += " ORDER BY " + (onlyDebtors ? BalanceColumn + " DESC" : "c.name COLLATE NOCASE");

        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<CustomerListItem>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CustomerListItem>> SearchAsync(
        string term,
        int limit = 100,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return await ListAsync(includeInactive: false, onlyDebtors: false, ct);
        }

        string[] tokens = term
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Take(6)
            .ToArray();

        if (tokens.Length == 0)
        {
            return await ListAsync(includeInactive: false, onlyDebtors: false, ct);
        }

        string[] clauses = tokens
            .Select((_, i) =>
                $"(lower(c.name) LIKE $t{i} OR lower(coalesce(c.phone,'')) LIKE $t{i})")
            .ToArray();

        string sql = ListSelect +
                     " WHERE c.is_active = 1 AND " +
                     string.Join(" AND ", clauses) +
                     " ORDER BY c.name COLLATE NOCASE LIMIT $limit";

        DynamicParameters parameters = new();
        for (int i = 0; i < tokens.Length; i++)
        {
            parameters.Add($"t{i}", $"%{tokens[i]}%");
        }
        parameters.Add("limit", limit);

        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<CustomerListItem>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Customer?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        return await connection.QueryFirstOrDefaultAsync<Customer>(new CommandDefinition(
            CustomerSelect + " WHERE id = $id", new { id }, cancellationToken: ct));
    }

    public async Task<int> InsertAsync(Customer customer, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        long id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
            INSERT INTO customers(uuid, name, phone, email, kra_pin, price_tier_id,
                                  credit_limit_cents, notes, is_active, import_batch_id)
            VALUES ($Uuid, $Name, $Phone, $Email, $KraPin, $PriceTierId,
                    $CreditLimitCents, $Notes, $IsActive, $ImportBatchId)
            RETURNING id", customer, cancellationToken: ct));
        return (int)id;
    }

    public async Task<int> InsertWithOpeningBalanceAsync(
        Customer customer,
        long openingBalanceCents,
        string businessDate,
        int? userId = null,
        CancellationToken ct = default)
    {
        if (openingBalanceCents < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(openingBalanceCents), "Opening balance cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(businessDate) ||
            !System.Text.RegularExpressions.Regex.IsMatch(businessDate, "^[0-9]{4}-[0-9]{2}-[0-9]{2}$"))
        {
            throw new ArgumentException("businessDate must be YYYY-MM-DD.", nameof(businessDate));
        }

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();
        try
        {
            long id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
                INSERT INTO customers(uuid, name, phone, email, kra_pin, price_tier_id,
                                      credit_limit_cents, notes, is_active, import_batch_id)
                VALUES ($Uuid, $Name, $Phone, $Email, $KraPin, $PriceTierId,
                        $CreditLimitCents, $Notes, $IsActive, $ImportBatchId)
                RETURNING id",
                customer, tx, cancellationToken: ct));

            if (openingBalanceCents > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(@"
                    INSERT INTO customer_ledger(customer_id, entry_type, amount_cents,
                                                notes, user_id, business_date)
                    VALUES ($CustomerId, 'OPENING', $AmountCents,
                            'Opening balance from notebook', $UserId, $BusinessDate)",
                    new
                    {
                        CustomerId = (int)id,
                        AmountCents = openingBalanceCents,
                        UserId = userId,
                        BusinessDate = businessDate,
                    },
                    tx, cancellationToken: ct));

                await connection.ExecuteAsync(new CommandDefinition(@"
                    INSERT INTO audit_log(user_id, action, entity_type, entity_id, new_value)
                    VALUES ($UserId, 'CUSTOMER_OPENING_BALANCE', 'customer', $CustomerId, $Amount)",
                    new
                    {
                        UserId = userId,
                        CustomerId = (int)id,
                        Amount = openingBalanceCents.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    },
                    tx, cancellationToken: ct));
            }

            tx.Commit();
            return (int)id;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task UpdateAsync(Customer customer, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE customers
               SET name               = $Name,
                   phone              = $Phone,
                   email              = $Email,
                   kra_pin            = $KraPin,
                   price_tier_id      = $PriceTierId,
                   credit_limit_cents = $CreditLimitCents,
                   notes              = $Notes,
                   is_active          = $IsActive
             WHERE id = $Id", customer, cancellationToken: ct));
    }

    public async Task ArchiveAsync(int id, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE customers SET is_active = 0 WHERE id = $id",
            new { id }, cancellationToken: ct));
    }

    public async Task<long> GetBalanceCentsAsync(int customerId, CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COALESCE(SUM(amount_cents), 0) FROM customer_ledger WHERE customer_id = $customerId",
            new { customerId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CustomerLedgerEntry>> ListLedgerAsync(
        int customerId,
        CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<LedgerRow>(new CommandDefinition(@"
            SELECT id              AS Id,
                   customer_id     AS CustomerId,
                   entry_type      AS EntryTypeString,
                   amount_cents    AS AmountCents,
                   sale_id         AS SaleId,
                   return_id       AS ReturnId,
                   cash_session_id AS CashSessionId,
                   method          AS Method,
                   reference       AS Reference,
                   notes           AS Notes,
                   user_id         AS UserId,
                   import_batch_id AS ImportBatchId,
                   business_date   AS BusinessDate,
                   created_at      AS CreatedAt
              FROM customer_ledger
             WHERE customer_id = $customerId
             ORDER BY business_date, id",
            new { customerId }, cancellationToken: ct));

        return rows.Select(r => r.ToEntry()).ToList();
    }

    private sealed class LedgerRow
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string EntryTypeString { get; set; } = "";
        public long AmountCents { get; set; }
        public int? SaleId { get; set; }
        public int? ReturnId { get; set; }
        public int? CashSessionId { get; set; }
        public string? Method { get; set; }
        public string? Reference { get; set; }
        public string? Notes { get; set; }
        public int? UserId { get; set; }
        public int? ImportBatchId { get; set; }
        public string BusinessDate { get; set; } = "";
        public string? CreatedAt { get; set; }

        public CustomerLedgerEntry ToEntry() => new()
        {
            Id = Id,
            CustomerId = CustomerId,
            EntryType = MapEntryType(EntryTypeString),
            AmountCents = AmountCents,
            SaleId = SaleId,
            ReturnId = ReturnId,
            CashSessionId = CashSessionId,
            Method = Method,
            Reference = Reference,
            Notes = Notes,
            UserId = UserId,
            ImportBatchId = ImportBatchId,
            BusinessDate = BusinessDate,
            CreatedAt = CreatedAt,
        };

        private static LedgerEntryType MapEntryType(string raw) => raw switch
        {
            "OPENING" => LedgerEntryType.Opening,
            "CHARGE" => LedgerEntryType.Charge,
            "PAYMENT" => LedgerEntryType.Payment,
            "ADJUSTMENT" => LedgerEntryType.Adjustment,
            _ => throw new InvalidOperationException(
                $"Unknown customer_ledger.entry_type: {raw}"),
        };
    }
}
