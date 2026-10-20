using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Sales;
using Pos.Domain;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Sales;

public sealed partial class SellService : ISellService
{
    private static readonly Regex BusinessDatePattern = BusinessDateRegex();
    private static readonly HashSet<string> AllowedMethods =
        new(StringComparer.Ordinal) { "CASH", "MPESA", "CARD", "BANK", "CREDIT", "OTHER" };

    private readonly DbConnectionFactory _factory;

    public SellService(DbConnectionFactory factory) => _factory = factory;

    public async Task<SaleResult> CompleteSaleAsync(SaleRequest request, CancellationToken ct = default)
    {
        ValidateRequest(request);

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            bool pricesIncludeTax = await GetSettingBoolAsync(connection, tx, "prices_include_tax", true, ct);
            bool allowNegativeStock = await GetSettingBoolAsync(connection, tx, "allow_negative_stock", false, ct);

            int priceTierId = await ResolvePriceTierAsync(connection, tx, request, ct);
            var snapshots = await LoadProductSnapshotsAsync(connection, tx, request.Lines, priceTierId, ct);
            var lines = ComputeLines(request.Lines, snapshots, pricesIncludeTax);

            long totalCents = lines.Sum(l => l.LineTotalCents);
            long taxTotal = lines.Sum(l => l.TaxCents);
            long subtotal = totalCents - taxTotal;

            if (!allowNegativeStock)
            {
                await ValidateStockAsync(connection, tx, lines, ct);
            }

            long paidTotal = request.Payments.Sum(p => p.AmountCents);
            if (paidTotal < totalCents)
            {
                throw new SaleValidationException(
                    $"Payment total ({Money.FromCents(paidTotal)}) is less than sale total ({Money.FromCents(totalCents)}).");
            }
            long change = paidTotal - totalCents;

            long nonCreditPaid = 0;
            long creditTotal = 0;
            foreach (PaymentRequest p in request.Payments)
            {
                if (p.Method == "CREDIT")
                {
                    creditTotal = checked(creditTotal + p.AmountCents);
                }
                else
                {
                    nonCreditPaid = checked(nonCreditPaid + p.AmountCents);
                }
            }

            if (creditTotal > 0)
            {
                if (request.CustomerId is null)
                {
                    throw new SaleValidationException("Credit payment requires a customer.");
                }

                // Credit may only cover the shortfall. If cash/mpesa/etc already
                // cover the total, no credit can be applied — that combination
                // is contradictory (you cannot both overpay and be in debt).
                if (nonCreditPaid > totalCents)
                {
                    throw new SaleValidationException(
                        "Cannot use credit when non-credit payments already cover the total.");
                }

                long requiredCredit = totalCents - nonCreditPaid;
                if (creditTotal != requiredCredit)
                {
                    throw new SaleValidationException(
                        $"Credit amount ({Money.FromCents(creditTotal)}) must exactly equal the shortfall ({Money.FromCents(requiredCredit)}).");
                }

                await ValidateCreditLimitAsync(connection, tx, request.CustomerId.Value, creditTotal, ct);
            }
            else
            {
                // No credit — overpayment is allowed, change is returned.
                // (change is already computed above as paidTotal - totalCents)
            }

            string receiptNumber = await AllocateReceiptNumberAsync(connection, tx, ct);

            long saleId = await InsertSaleHeaderAsync(
                connection, tx, request, receiptNumber, priceTierId, pricesIncludeTax,
                subtotal, taxTotal, totalCents, ct);

            foreach (ComputedLine line in lines)
            {
                await InsertSaleLineAsync(connection, tx, (int)saleId, line, ct);
                await InsertSaleMovementAsync(connection, tx, (int)saleId, line, request, ct);
            }

            foreach (PaymentRequest p in request.Payments)
            {
                await InsertPaymentAsync(connection, tx, (int)saleId, p, request.BusinessDate, ct);
            }

            if (creditTotal > 0)
            {
                await InsertCreditChargeAsync(
                    connection, tx, (int)saleId, request.CustomerId!.Value,
                    creditTotal, request.BusinessDate, request.UserId, ct);
            }

            foreach (ComputedLine line in lines.Where(l => l.PriceOverridden))
            {
                await InsertAuditAsync(connection, tx, request.UserId, "PRICE_OVERRIDE", "sale", (int)saleId,
                    oldValue: $"product {line.ProductId} tier={line.TierPriceCentsCents} override={line.UnitPriceCents}",
                    newValue: null, ct);
            }

            await InsertAuditAsync(connection, tx, request.UserId, "SALE_COMPLETED", "sale", (int)saleId,
                oldValue: null,
                newValue: $"{receiptNumber} total {Money.FromCents(totalCents)} method count {request.Payments.Count}",
                ct);

            tx.Commit();

            return new SaleResult(
                SaleId: (int)saleId,
                ReceiptNumber: receiptNumber,
                SubtotalCents: subtotal,
                DiscountCents: 0,
                TaxCents: taxTotal,
                TotalCents: totalCents,
                PaidCents: paidTotal,
                ChangeCents: change);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ============ Validation ============

    private static void ValidateRequest(SaleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.BusinessDate) ||
            !BusinessDatePattern.IsMatch(request.BusinessDate))
        {
            throw new SaleValidationException("business_date must be YYYY-MM-DD.");
        }

        if (request.Lines.Count == 0)
        {
            throw new SaleValidationException("A sale must have at least one line.");
        }

        if (request.Payments.Count == 0)
        {
            throw new SaleValidationException("A sale must have at least one payment.");
        }

        HashSet<int> seenProducts = new();
        foreach (SaleLineRequest line in request.Lines)
        {
            if (line.QtyMilli <= 0)
            {
                throw new SaleValidationException($"Quantity must be positive (product {line.ProductId}).");
            }
            if (line.UnitPriceCentsOverride is < 0)
            {
                throw new SaleValidationException($"Price override cannot be negative (product {line.ProductId}).");
            }
            if (line.DiscountCents < 0)
            {
                throw new SaleValidationException($"Discount cannot be negative (product {line.ProductId}).");
            }
            if (!seenProducts.Add(line.ProductId))
            {
                throw new SaleValidationException(
                    $"Duplicate product on sale: {line.ProductId}. Merge into one line.");
            }
        }

        foreach (PaymentRequest p in request.Payments)
        {
            if (p.AmountCents <= 0)
            {
                throw new SaleValidationException("Every payment amount must be greater than zero.");
            }
            if (!AllowedMethods.Contains(p.Method))
            {
                throw new SaleValidationException(
                    $"Unknown payment method '{p.Method}'. Use CASH, MPESA, CARD, BANK, CREDIT or OTHER.");
            }
            if ((p.Method == "MPESA" || p.Method == "CARD" || p.Method == "BANK") &&
                string.IsNullOrWhiteSpace(p.Reference))
            {
                throw new SaleValidationException($"{p.Method} payment requires a reference.");
            }
        }
    }

    // ============ Settings + tier resolution ============

    private static async Task<bool> GetSettingBoolAsync(
        SqliteConnection c, SqliteTransaction tx, string key, bool defaultValue, CancellationToken ct)
    {
        string? raw = await c.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT value FROM settings WHERE key = $key", new { key }, tx, cancellationToken: ct));
        if (raw is null)
        {
            return defaultValue;
        }
        return raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> ResolvePriceTierAsync(
        SqliteConnection c, SqliteTransaction tx, SaleRequest request, CancellationToken ct)
    {
        if (request.PriceTierId is not null)
        {
            return request.PriceTierId.Value;
        }

        if (request.CustomerId is not null)
        {
            int? customerTier = await c.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT price_tier_id FROM customers WHERE id = $id",
                new { id = request.CustomerId.Value }, tx, cancellationToken: ct));
            if (customerTier is not null)
            {
                return customerTier.Value;
            }
        }

        int? defaultTier = await c.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT id FROM price_tiers WHERE is_default = 1 LIMIT 1",
            transaction: tx, cancellationToken: ct));
        if (defaultTier is null)
        {
            throw new SaleValidationException("No default price tier configured.");
        }

        return defaultTier.Value;
    }

    // ============ Product loading + line computation ============

    private sealed class ProductSnapshot
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Sku { get; set; }
        public int BaseUnitId { get; set; }
        public long AvgCostCents { get; set; }
        public long StockMilli { get; set; }
        public int TaxRateBp { get; set; }
        public long? TierPriceCents { get; set; }
        public long? DefaultTierPriceCents { get; set; }
    }

    private sealed class UnitFactorRow
    {
        public int ProductId { get; set; }
        public int UnitId { get; set; }
        public long FactorMilli { get; set; }
    }

    private async Task<Dictionary<int, ProductSnapshot>> LoadProductSnapshotsAsync(
        SqliteConnection c, SqliteTransaction tx, IReadOnlyList<SaleLineRequest> lines,
        int priceTierId, CancellationToken ct)
    {
        int[] productIds = lines.Select(l => l.ProductId).Distinct().ToArray();
        DynamicParameters p = new();
        string[] placeholders = productIds.Select((_, i) =>
        {
            p.Add($"id{i}", productIds[i]);
            return $"$id{i}";
        }).ToArray();
        p.Add("tierId", priceTierId);

        string sql = $@"
            SELECT p.id AS Id, p.name AS Name, p.sku AS Sku,
                   p.base_unit_id AS BaseUnitId, p.avg_cost_cents AS AvgCostCents,
                   p.stock_milli AS StockMilli,
                   COALESCE(tc.rate_bp, 0) AS TaxRateBp,
                   pp.price_cents AS TierPriceCents,
                   defpp.price_cents AS DefaultTierPriceCents
              FROM products p
              LEFT JOIN tax_classes tc ON tc.id = p.tax_class_id
              LEFT JOIN product_prices pp ON pp.product_id = p.id AND pp.tier_id = $tierId
              LEFT JOIN price_tiers def_pt ON def_pt.is_default = 1
              LEFT JOIN product_prices defpp ON defpp.product_id = p.id AND defpp.tier_id = def_pt.id
             WHERE p.id IN ({string.Join(",", placeholders)}) AND p.is_active = 1";

        var rows = await c.QueryAsync<ProductSnapshot>(
            new CommandDefinition(sql, p, tx, cancellationToken: ct));

        Dictionary<int, ProductSnapshot> byId = rows.ToDictionary(r => r.Id);
        if (byId.Count != productIds.Length)
        {
            int[] missing = productIds.Where(id => !byId.ContainsKey(id)).ToArray();
            throw new SaleValidationException(
                $"Unknown or archived product id(s): {string.Join(", ", missing)}.");
        }

        return byId;
    }

    private sealed class ComputedLine
    {
        public int ProductId;
        public string ProductName = "";
        public string? ProductSku;
        public int UnitId;
        public long QtyMilli;
        public long BaseQtyMilli;
        public long UnitPriceCents;
        public long UnitCostCents;
        public long DiscountCents;
        public int TaxRateBp;
        public long TaxCents;
        public long LineTotalCents;
        public string? Notes;
        public bool PriceOverridden;
        public long? TierPriceCentsCents;
    }

    private static List<ComputedLine> ComputeLines(
        IReadOnlyList<SaleLineRequest> requestLines,
        Dictionary<int, ProductSnapshot> products,
        bool pricesIncludeTax)
    {
        List<ComputedLine> result = new();

        foreach (SaleLineRequest lr in requestLines)
        {
            ProductSnapshot snapshot = products[lr.ProductId];

            long qty = lr.QtyMilli;

            if (lr.UnitId != snapshot.BaseUnitId)
            {
                // Unit conversion factor for a non-base selling unit is handled by the caller
                // today; the UI computes base_qty for selling units. If the caller passes a
                // non-base unit, it must also pass the factor via the UI, which multiplies
                // qty into base. In future this method will query product_units. For v1 the
                // UI always sends the base unit; anything else is rejected.
                throw new SaleValidationException(
                    $"Selling unit {lr.UnitId} differs from product's base unit. Multi-unit selling is not supported in v1.");
            }

            long baseQty = qty;

            long tierPrice = lr.UnitPriceCentsOverride
                ?? snapshot.TierPriceCents
                ?? snapshot.DefaultTierPriceCents
                ?? throw new SaleValidationException($"No price set for product '{snapshot.Name}'.");

            bool overridden = lr.UnitPriceCentsOverride is not null;

            long gross = Money.RoundHalfUpDiv(checked(tierPrice * qty), Quantity.MilliPerUnit);
            long net = gross - lr.DiscountCents;
            if (net < 0)
            {
                throw new SaleValidationException(
                    $"Discount exceeds price for product '{snapshot.Name}'.");
            }

            long taxCents;
            long lineTotal;
            if (pricesIncludeTax)
            {
                lineTotal = net;
                long denom = checked(10_000 + snapshot.TaxRateBp);
                taxCents = denom == 0 ? 0 : Money.RoundHalfUpDiv(checked(net * snapshot.TaxRateBp), denom);
            }
            else
            {
                taxCents = Money.RoundHalfUpDiv(checked(net * snapshot.TaxRateBp), 10_000);
                lineTotal = checked(net + taxCents);
            }

            result.Add(new ComputedLine
            {
                ProductId = snapshot.Id,
                ProductName = snapshot.Name,
                ProductSku = snapshot.Sku,
                UnitId = lr.UnitId,
                QtyMilli = qty,
                BaseQtyMilli = baseQty,
                UnitPriceCents = tierPrice,
                UnitCostCents = snapshot.AvgCostCents,
                DiscountCents = lr.DiscountCents,
                TaxRateBp = snapshot.TaxRateBp,
                TaxCents = taxCents,
                LineTotalCents = lineTotal,
                Notes = lr.Notes,
                PriceOverridden = overridden,
                TierPriceCentsCents = snapshot.TierPriceCents,
            });
        }

        return result;
    }

    // ============ Stock + credit checks ============

    private static async Task ValidateStockAsync(
        SqliteConnection c, SqliteTransaction tx, List<ComputedLine> lines, CancellationToken ct)
    {
        foreach (ComputedLine line in lines)
        {
            long onHand = await c.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT stock_milli FROM products WHERE id = $id",
                new { id = line.ProductId }, tx, cancellationToken: ct));
            if (onHand < line.BaseQtyMilli)
            {
                throw new SaleValidationException(
                    $"Not enough stock for '{line.ProductName}': on hand {new Quantity(onHand)}, need {new Quantity(line.BaseQtyMilli)}.");
            }
        }
    }

    private static async Task ValidateCreditLimitAsync(
        SqliteConnection c, SqliteTransaction tx, int customerId, long creditCents, CancellationToken ct)
    {
        var row = await c.QuerySingleOrDefaultAsync<(long BalanceCents, long CreditLimitCents)?>(new CommandDefinition(@"
            SELECT (SELECT COALESCE(SUM(amount_cents),0) FROM customer_ledger WHERE customer_id = $id) AS BalanceCents,
                   credit_limit_cents AS CreditLimitCents
              FROM customers WHERE id = $id",
            new { id = customerId }, tx, cancellationToken: ct));

        if (row is null)
        {
            throw new SaleValidationException($"Customer {customerId} does not exist.");
        }

        (long balance, long limit) = row.Value;
        if (balance + creditCents > limit)
        {
            throw new SaleValidationException(
                $"Credit limit exceeded. Current balance {Money.FromCents(balance)}, limit {Money.FromCents(limit)}, requested credit {Money.FromCents(creditCents)}.");
        }
    }

    // ============ Inserts ============

    private static async Task<string> AllocateReceiptNumberAsync(
        SqliteConnection c, SqliteTransaction tx, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE document_sequences SET next_number = next_number + 1 WHERE doc_type = 'SALE'",
            transaction: tx, cancellationToken: ct));
        long number = await c.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT next_number - 1 FROM document_sequences WHERE doc_type = 'SALE'",
            transaction: tx, cancellationToken: ct));
        string prefix = await c.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT prefix FROM document_sequences WHERE doc_type = 'SALE'",
            transaction: tx, cancellationToken: ct)) ?? "RCT-";
        return prefix + number.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<long> InsertSaleHeaderAsync(
        SqliteConnection c, SqliteTransaction tx, SaleRequest request, string receiptNumber,
        int priceTierId, bool pricesIncludeTax, long subtotal, long tax, long total, CancellationToken ct)
    {
        return await c.ExecuteScalarAsync<long>(new CommandDefinition(@"
            INSERT INTO sales(uuid, receipt_number, status, customer_id, user_id, cash_session_id,
                              price_tier_id, prices_include_tax, subtotal_cents, discount_cents,
                              tax_cents, total_cents, business_date)
            VALUES ($Uuid, $ReceiptNumber, 'COMPLETED', $CustomerId, $UserId, $CashSessionId,
                    $PriceTierId, $PricesIncludeTax, $Subtotal, 0,
                    $Tax, $Total, $BusinessDate)
            RETURNING id",
            new
            {
                Uuid = Guid.NewGuid().ToString("N"),
                ReceiptNumber = receiptNumber,
                request.CustomerId,
                request.UserId,
                request.CashSessionId,
                PriceTierId = priceTierId,
                PricesIncludeTax = pricesIncludeTax ? 1 : 0,
                Subtotal = subtotal,
                Tax = tax,
                Total = total,
                request.BusinessDate,
            }, tx, cancellationToken: ct));
    }

    private static async Task InsertSaleLineAsync(
        SqliteConnection c, SqliteTransaction tx, int saleId, ComputedLine l, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO sale_items(sale_id, product_id, product_name, product_sku, unit_id,
                                   qty_milli, base_qty_milli, unit_price_cents, unit_cost_cents,
                                   discount_cents, tax_rate_bp, line_total_cents, notes)
            VALUES ($SaleId, $ProductId, $ProductName, $ProductSku, $UnitId,
                    $QtyMilli, $BaseQtyMilli, $UnitPrice, $UnitCost,
                    $Discount, $TaxRateBp, $LineTotal, $Notes)",
            new
            {
                SaleId = saleId,
                l.ProductId,
                l.ProductName,
                l.ProductSku,
                l.UnitId,
                l.QtyMilli,
                l.BaseQtyMilli,
                UnitPrice = l.UnitPriceCents,
                UnitCost = l.UnitCostCents,
                Discount = l.DiscountCents,
                l.TaxRateBp,
                LineTotal = l.LineTotalCents,
                l.Notes,
            }, tx, cancellationToken: ct));
    }

    private static async Task InsertSaleMovementAsync(
        SqliteConnection c, SqliteTransaction tx, int saleId, ComputedLine l, SaleRequest request, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO stock_movements(product_id, movement_type, qty_milli, unit_cost_cents,
                                        ref_type, ref_id, user_id, business_date)
            VALUES ($ProductId, 'SALE', $QtyMilli, $UnitCost,
                    'SALE', $SaleId, $UserId, $BusinessDate)",
            new
            {
                l.ProductId,
                QtyMilli = -l.BaseQtyMilli,
                UnitCost = l.UnitCostCents,
                SaleId = saleId,
                request.UserId,
                request.BusinessDate,
            }, tx, cancellationToken: ct));
    }

    private static async Task InsertPaymentAsync(
        SqliteConnection c, SqliteTransaction tx, int saleId, PaymentRequest p, string businessDate, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO payments(sale_id, method, amount_cents, reference, business_date)
            VALUES ($SaleId, $Method, $Amount, $Reference, $BusinessDate)",
            new
            {
                SaleId = saleId,
                p.Method,
                Amount = p.AmountCents,
                p.Reference,
                BusinessDate = businessDate,
            }, tx, cancellationToken: ct));
    }

    private static async Task InsertCreditChargeAsync(
        SqliteConnection c, SqliteTransaction tx, int saleId, int customerId,
        long creditCents, string businessDate, int userId, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO customer_ledger(customer_id, entry_type, amount_cents, sale_id, method,
                                        user_id, business_date)
            VALUES ($CustomerId, 'CHARGE', $Amount, $SaleId, 'CREDIT', $UserId, $BusinessDate)",
            new
            {
                CustomerId = customerId,
                Amount = creditCents,
                SaleId = saleId,
                UserId = userId,
                BusinessDate = businessDate,
            }, tx, cancellationToken: ct));
    }

    private static async Task InsertAuditAsync(
        SqliteConnection c, SqliteTransaction tx, int userId, string action, string entityType,
        int entityId, string? oldValue, string? newValue, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO audit_log(user_id, action, entity_type, entity_id, old_value, new_value)
            VALUES ($UserId, $Action, $EntityType, $EntityId, $OldValue, $NewValue)",
            new
            {
                UserId = userId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                OldValue = oldValue,
                NewValue = newValue,
            }, tx, cancellationToken: ct));
    }

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}$")]
    private static partial Regex BusinessDateRegex();
}

