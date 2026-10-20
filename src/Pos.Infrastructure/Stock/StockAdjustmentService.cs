using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Stock;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Stock;

public sealed class StockAdjustmentService : IStockAdjustmentService
{
    private readonly DbConnectionFactory _factory;

    public StockAdjustmentService(DbConnectionFactory factory) => _factory = factory;

    public async Task<StockAdjustmentResult> AdjustAsync(
        StockAdjustmentRequest request,
        CancellationToken ct = default)
    {
        ValidateRequest(request);

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            ProductRow? product = await connection.QueryFirstOrDefaultAsync<ProductRow>(new CommandDefinition(
                "SELECT id AS Id, name AS Name, stock_milli AS StockMilli FROM products WHERE id = $id AND is_active = 1",
                new { id = request.ProductId }, tx, cancellationToken: ct));

            if (product is null)
            {
                throw new StockAdjustmentException($"Product {request.ProductId} not found or is archived.");
            }

            long newStock = product.StockMilli + request.DeltaMilli;
            if (newStock < 0)
            {
                throw new StockAdjustmentException(
                    $"Adjustment would make stock negative ({newStock / 1000m:0.###}). Reduce the quantity.");
            }

            string movementType = MapMovementType(request.ReasonCategory, request.DeltaMilli);

            long movementId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
                INSERT INTO stock_movements(product_id, movement_type, qty_milli,
                                            ref_type, reason, user_id, business_date)
                VALUES ($productId, $movementType, $qtyMilli,
                        'ADJUSTMENT', $reason, $userId, $businessDate)
                RETURNING id",
                new
                {
                    productId = request.ProductId,
                    movementType,
                    qtyMilli = request.DeltaMilli,
                    reason = request.Notes.Trim(),
                    userId = request.UserId,
                    businessDate = request.BusinessDate,
                },
                tx, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO audit_log(user_id, action, entity_type, entity_id, old_value, new_value)
                VALUES ($userId, 'STOCK_ADJUSTED', 'stock_movement', $movementId, $oldValue, $newValue)",
                new
                {
                    userId = request.UserId,
                    movementId = (int)movementId,
                    oldValue = $"{product.Name}: {product.StockMilli / 1000m:0.###}",
                    newValue = $"{newStock / 1000m:0.###} ({request.ReasonCategory}: {request.Notes.Trim()})",
                },
                tx, cancellationToken: ct));

            tx.Commit();

            return new StockAdjustmentResult(
                MovementId: (int)movementId,
                PreviousStockMilli: product.StockMilli,
                NewStockMilli: newStock,
                MovementType: movementType);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static string MapMovementType(string reason, long delta)
    {
        return reason switch
        {
            AdjustmentReasons.Damaged => "DAMAGE",
            AdjustmentReasons.Lost => "ADJUST_LOSS",
            AdjustmentReasons.Found => "ADJUST_GAIN",
            AdjustmentReasons.Counted => delta > 0 ? "ADJUST_GAIN" : "ADJUST_LOSS",
            _ => throw new StockAdjustmentException($"Unknown reason: {reason}"),
        };
    }

    private static void ValidateRequest(StockAdjustmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ProductId <= 0)
            throw new StockAdjustmentException("Product is required.");

        if (request.UserId <= 0)
            throw new StockAdjustmentException("User is required.");

        if (!AdjustmentReasons.All.Contains(request.ReasonCategory))
            throw new StockAdjustmentException($"Unknown reason: {request.ReasonCategory}");

        if (request.DeltaMilli == 0)
            throw new StockAdjustmentException("Adjustment quantity cannot be zero.");

        if (request.ReasonCategory == AdjustmentReasons.Damaged && request.DeltaMilli > 0)
            throw new StockAdjustmentException("Damaged quantity must be negative (products lost).");

        if (request.ReasonCategory == AdjustmentReasons.Lost && request.DeltaMilli > 0)
            throw new StockAdjustmentException("Lost quantity must be negative (products missing).");

        if (request.ReasonCategory == AdjustmentReasons.Found && request.DeltaMilli < 0)
            throw new StockAdjustmentException("Found quantity must be positive (products appeared).");

        if (string.IsNullOrWhiteSpace(request.Notes))
            throw new StockAdjustmentException("A short note explaining the adjustment is required.");

        if (request.Notes.Trim().Length > 200)
            throw new StockAdjustmentException("Note is too long (200 characters max).");

        if (string.IsNullOrWhiteSpace(request.BusinessDate) || request.BusinessDate.Length != 10)
            throw new StockAdjustmentException("Date must be YYYY-MM-DD.");
    }

    private sealed class ProductRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public long StockMilli { get; set; }
    }
}
