using System.Text;
using Dapper;
using MoizPos.Application.Abstractions;
using MoizPos.Domain.Entities;

namespace MoizPos.Infrastructure.Repositories;

/// <summary>Read side of purchasing. Writes live in <see cref="PurchaseWriteRepository"/>.</summary>
public sealed class PurchaseRepository : IPurchaseRepository
{
    private const string SelectColumns = """
        id                AS Id,
        supplier_id       AS SupplierId,
        product_id        AS ProductId,
        purchase_date_utc AS PurchaseDateUtc,
        unit_cost         AS UnitCost,
        quantity          AS Quantity,
        total             AS Total,
        returned_qty      AS ReturnedQty,
        user_id           AS UserId
        """;

    private readonly IDbConnectionFactory _connectionFactory;

    public PurchaseRepository(IDbConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public async Task<(IReadOnlyList<Purchase> Items, int TotalItems)> SearchAsync(
        long? supplierId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var where = new StringBuilder("WHERE 1 = 1");

        if (supplierId is not null)
        {
            where.Append(" AND supplier_id = @supplierId");
        }

        if (fromUtc is not null)
        {
            where.Append(" AND purchase_date_utc >= @fromUtc");
        }

        if (toUtc is not null)
        {
            // Half-open: the caller's range end is exclusive (research.md R6).
            where.Append(" AND purchase_date_utc < @toUtc");
        }

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        await using var reader = await connection.QueryMultipleAsync(
            $"""
             SELECT {SelectColumns}
             FROM purchases
             {where}
             ORDER BY purchase_date_utc DESC, id DESC
             LIMIT @limit OFFSET @offset;

             SELECT COUNT(*) FROM purchases {where};
             """,
            new { supplierId, fromUtc, toUtc, limit = pageSize, offset = (page - 1) * pageSize });

        var items = (await reader.ReadAsync<Purchase>()).AsList();
        var total = await reader.ReadSingleAsync<int>();

        return (items, total);
    }

    public async Task<Purchase?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<Purchase>(
            $"SELECT {SelectColumns} FROM purchases WHERE id = @id LIMIT 1;", new { id });
    }

    /// <summary>
    /// Purchases and payments interleaved in date order, so the screen can show a running
    /// payable exactly the way the shopkeeper's own register does (FR-010).
    /// </summary>
    public async Task<IReadOnlyList<SupplierLedgerRow>> LedgerForSupplierAsync(
        long supplierId,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<SupplierLedgerRow>(
            """
            SELECT p.purchase_date_utc AS EntryDateUtc,
                   'Purchase'          AS EntryType,
                   p.id                AS ReferenceId,
                   pr.name             AS Description,
                   p.total             AS PurchaseAmount,
                   0                   AS PaymentAmount
            FROM purchases p
            JOIN products pr ON pr.id = p.product_id
            WHERE p.supplier_id = @supplierId
              AND (@fromUtc IS NULL OR p.purchase_date_utc >= @fromUtc)
              AND (@toUtc   IS NULL OR p.purchase_date_utc <  @toUtc)

            UNION ALL

            SELECT sp.payment_date_utc AS EntryDateUtc,
                   'Payment'           AS EntryType,
                   sp.id               AS ReferenceId,
                   sp.note             AS Description,
                   0                   AS PurchaseAmount,
                   sp.amount           AS PaymentAmount
            FROM supplier_payments sp
            WHERE sp.supplier_id = @supplierId
              AND (@fromUtc IS NULL OR sp.payment_date_utc >= @fromUtc)
              AND (@toUtc   IS NULL OR sp.payment_date_utc <  @toUtc)

            ORDER BY EntryDateUtc, ReferenceId;
            """,
            new { supplierId, fromUtc, toUtc });

        return rows.AsList();
    }
}
