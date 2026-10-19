using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Stock;
using Pos.Domain;
using Pos.Domain.Stock;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Stock;

public sealed partial class ReceiveGoodsService : IReceiveGoods
{
    private static readonly Regex BusinessDatePattern = BusinessDateRegex();

    private readonly DbConnectionFactory _factory;

    public ReceiveGoodsService(DbConnectionFactory factory) => _factory = factory;

    public async Task<ReceiveGoodsResult> ExecuteAsync(ReceiveGoodsRequest request, CancellationToken ct = default)
    {
        ValidateRequest(request);

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            string grnNumber = await AllocateGrnNumberAsync(connection, tx, ct);

            long totalCents = 0;
            foreach (ReceiveGoodsLine line in request.Lines)
            {
                long lineTotal = Money.RoundHalfUpDiv(
                    checked(line.UnitCostCents * line.QtyMilli),
                    Quantity.MilliPerUnit);
                totalCents = checked(totalCents + lineTotal);
            }

            long receiptId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
                INSERT INTO goods_receipts(grn_number, supplier_id, supplier_ref, total_cents, paid_cents,
                                           user_id, business_date)
                VALUES ($GrnNumber, $SupplierId, $SupplierRef, $TotalCents, 0, $UserId, $BusinessDate)
                RETURNING id",
                new
                {
                    GrnNumber = grnNumber,
                    request.SupplierId,
                    request.SupplierRef,
                    TotalCents = totalCents,
                    request.UserId,
                    request.BusinessDate,
                },
                tx, cancellationToken: ct));

            foreach (ReceiveGoodsLine line in request.Lines)
            {
                await InsertLineAsync(connection, tx, (int)receiptId, line, request, ct);
            }

            tx.Commit();

            return new ReceiveGoodsResult((int)receiptId, grnNumber, totalCents);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static async Task InsertLineAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        int receiptId,
        ReceiveGoodsLine line,
        ReceiveGoodsRequest request,
        CancellationToken ct)
    {
        ProductSnapshot? product = await connection.QueryFirstOrDefaultAsync<ProductSnapshot>(new CommandDefinition(@"
            SELECT id AS Id, stock_milli AS StockMilli, avg_cost_cents AS AvgCostCents
              FROM products WHERE id = $id",
            new { id = line.ProductId },
            tx, cancellationToken: ct));

        if (product is null)
        {
            throw new ReceiveGoodsValidationException(
                $"Product id {line.ProductId} does not exist.");
        }

        long newAvg = WeightedAverageCost.Calculate(
            product.StockMilli, product.AvgCostCents, line.QtyMilli, line.UnitCostCents);

        await connection.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO goods_receipt_items(receipt_id, product_id, qty_milli, unit_cost_cents)
            VALUES ($ReceiptId, $ProductId, $QtyMilli, $UnitCostCents)",
            new
            {
                ReceiptId = receiptId,
                ProductId = line.ProductId,
                QtyMilli = line.QtyMilli,
                UnitCostCents = line.UnitCostCents,
            },
            tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO stock_movements(product_id, movement_type, qty_milli, unit_cost_cents,
                                        ref_type, ref_id, user_id, business_date)
            VALUES ($ProductId, 'PURCHASE', $QtyMilli, $UnitCostCents,
                    'GRN', $ReceiptId, $UserId, $BusinessDate)",
            new
            {
                ProductId = line.ProductId,
                QtyMilli = line.QtyMilli,
                UnitCostCents = line.UnitCostCents,
                ReceiptId = receiptId,
                UserId = request.UserId,
                BusinessDate = request.BusinessDate,
            },
            tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(@"
            UPDATE products
               SET avg_cost_cents  = $NewAvg,
                   last_cost_cents = $LastCost,
                   updated_at      = strftime('%Y-%m-%dT%H:%M:%SZ','now')
             WHERE id = $Id",
            new
            {
                NewAvg = newAvg,
                LastCost = line.UnitCostCents,
                Id = line.ProductId,
            },
            tx, cancellationToken: ct));
    }

    private static async Task<string> AllocateGrnNumberAsync(
        SqliteConnection connection, SqliteTransaction tx, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE document_sequences SET next_number = next_number + 1 WHERE doc_type = 'GRN'",
            transaction: tx, cancellationToken: ct));

        long number = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT next_number - 1 FROM document_sequences WHERE doc_type = 'GRN'",
            transaction: tx, cancellationToken: ct));

        string prefix = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT prefix FROM document_sequences WHERE doc_type = 'GRN'",
            transaction: tx, cancellationToken: ct)) ?? "GRN-";

        return prefix + number.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ValidateRequest(ReceiveGoodsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.BusinessDate) || !BusinessDatePattern.IsMatch(request.BusinessDate))
        {
            throw new ReceiveGoodsValidationException("business_date must be YYYY-MM-DD.");
        }

        if (request.Lines.Count == 0)
        {
            throw new ReceiveGoodsValidationException("A receipt must contain at least one line.");
        }

        HashSet<int> seen = new();
        foreach (ReceiveGoodsLine line in request.Lines)
        {
            if (line.QtyMilli <= 0)
            {
                throw new ReceiveGoodsValidationException(
                    $"Quantity must be positive (product {line.ProductId}).");
            }

            if (line.UnitCostCents < 0)
            {
                throw new ReceiveGoodsValidationException(
                    $"Unit cost cannot be negative (product {line.ProductId}).");
            }

            if (!seen.Add(line.ProductId))
            {
                throw new ReceiveGoodsValidationException(
                    $"Duplicate product on receipt: {line.ProductId}. Merge into one line.");
            }
        }
    }

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}$")]
    private static partial Regex BusinessDateRegex();

    private sealed class ProductSnapshot
    {
        public int Id { get; set; }
        public long StockMilli { get; set; }
        public long AvgCostCents { get; set; }
    }
}
