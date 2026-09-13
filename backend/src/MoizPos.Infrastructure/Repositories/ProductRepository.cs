using System.Text;
using Dapper;
using MoizPos.Application.Abstractions;
using MoizPos.Domain.Entities;
using MoizPos.Domain.Enums;

namespace MoizPos.Infrastructure.Repositories;

/// <inheritdoc />
public sealed class ProductRepository : IProductRepository
{
    /// <summary>
    /// The price to quote. A wholesale sale is quoted wholesale_price; everything else gets the
    /// counter price. Resolved in SQL so the caller is handed one price — the one that applies —
    /// rather than both (FR-040: a Staff DTO may not carry a wholesale figure).
    ///
    /// <para>A product with no wholesale price set falls back to the counter price, so switching
    /// to wholesale can never quote zero for stock the owner has not priced for bulk yet.</para>
    /// </summary>
    private static string PriceColumn(SaleType saleType) =>
        saleType == SaleType.Wholesale
            ? "CASE WHEN p.wholesale_price > 0 THEN p.wholesale_price ELSE p.sale_price END"
            : "p.sale_price";

    /// <summary>
    /// The projected columns. SalePrice is whichever price applies to the sale being made, so a
    /// caller receives one price rather than a menu of them.
    /// </summary>
    private static string SelectColumns(SaleType saleType) => $"""
        p.id                  AS Id,
        p.name                AS Name,
        p.category_id         AS CategoryId,
        c.name                AS Category,
        p.brand_id            AS BrandId,
        b.name                AS Brand,
        p.model               AS Model,
        p.barcode             AS Barcode,
        p.image_path          AS ImagePath,
        p.cost_price          AS CostPrice,
        p.wholesale_price     AS WholesalePrice,
        p.retail_price        AS RetailPrice,
        {PriceColumn(saleType)} AS SalePrice,
        p.quantity_on_hand    AS QuantityOnHand,
        p.min_stock_threshold AS MinStockThreshold,
        p.supplier_id         AS SupplierId,
        s.name                AS SupplierName,
        p.is_active           AS IsActive
        """;

    private readonly IDbConnectionFactory _connectionFactory;

    public ProductRepository(IDbConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public async Task<(IReadOnlyList<ProductRow> Items, int TotalItems)> SearchAsync(
        ProductQuery query,
        CancellationToken cancellationToken = default)
    {
        var where = new StringBuilder("WHERE 1 = 1");
        var parameters = new DynamicParameters();

        if (!query.IncludeInactive)
        {
            where.Append(" AND p.is_active = TRUE");
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // One search box across name, brand, model, category and barcode (FR-003).
            // LIKE rather than MATCH...AGAINST: the shopkeeper types partial words ("brai" for
            // "braided") and fulltext boolean mode would miss those without wildcards, while a
            // 5,000-row catalogue is far too small for the index to matter.
            where.Append("""
                 AND (p.name LIKE @search
                   OR b.name LIKE @search
                   OR p.model LIKE @search
                   OR c.name LIKE @search
                   OR p.barcode = @exactSearch)
                """);

            parameters.Add("search", $"%{query.Search.Trim()}%");
            parameters.Add("exactSearch", query.Search.Trim());
        }

        if (query.CategoryId.HasValue)
        {
            where.Append(" AND p.category_id = @categoryId");
            parameters.Add("categoryId", query.CategoryId.Value);
        }

        if (query.BrandId.HasValue)
        {
            where.Append(" AND p.brand_id = @brandId");
            parameters.Add("brandId", query.BrandId.Value);
        }

        if (query.LowStockOnly)
        {
            where.Append(" AND p.quantity_on_hand <= p.min_stock_threshold");
        }

        parameters.Add("offset", (query.Page - 1) * query.PageSize);
        parameters.Add("limit", query.PageSize);

        var sql = $"""
            SELECT {SelectColumns(query.SaleType)}
            FROM products p
            JOIN categories c ON c.id = p.category_id
            LEFT JOIN brands b ON b.id = p.brand_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            {where}
            ORDER BY p.name, p.id
            LIMIT @limit OFFSET @offset;

            SELECT COUNT(*)
            FROM products p
            JOIN categories c ON c.id = p.category_id
            LEFT JOIN brands b ON b.id = p.brand_id
            {where};
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var reader = await connection.QueryMultipleAsync(sql, parameters);

        var items = (await reader.ReadAsync<ProductRow>()).AsList();
        var total = await reader.ReadSingleAsync<int>();

        return (items, total);
    }

    public async Task<ProductRow?> FindByIdAsync(
        long id,
        SaleType saleType = SaleType.Retail,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<ProductRow>(
            $"""
             SELECT {SelectColumns(saleType)}
             FROM products p
             JOIN categories c ON c.id = p.category_id
             LEFT JOIN brands b ON b.id = p.brand_id
             LEFT JOIN suppliers s ON s.id = p.supplier_id
             WHERE p.id = @id
             LIMIT 1;
             """,
            new { id });
    }

    public async Task<ProductRow?> FindByBarcodeAsync(
        string barcode,
        SaleType saleType = SaleType.Retail,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<ProductRow>(
            $"""
             SELECT {SelectColumns(saleType)}
             FROM products p
             JOIN categories c ON c.id = p.category_id
             LEFT JOIN brands b ON b.id = p.brand_id
             LEFT JOIN suppliers s ON s.id = p.supplier_id
             WHERE p.barcode = @barcode AND p.is_active = TRUE
             LIMIT 1;
             """,
            new { barcode });
    }

    public async Task<long> CreateAsync(Product product, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO products
                (name, category_id, brand_id, model, barcode, image_path, cost_price, wholesale_price,
                 retail_price, sale_price, quantity_on_hand, min_stock_threshold, supplier_id,
                 is_active, created_at_utc)
            VALUES
                (@Name, @CategoryId, @BrandId, @Model, @Barcode, @ImagePath, @CostPrice, @WholesalePrice,
                 @RetailPrice, @SalePrice, @QuantityOnHand, @MinStockThreshold, @SupplierId,
                 TRUE, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """,
            product);
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        // Deliberately does NOT touch quantity_on_hand or cost_price: stock moves only through
        // purchases, sales, returns and audited adjustments, and cost moves only through a
        // purchase (FR-011a). Editing a product must never be a back door into either.
        await connection.ExecuteAsync(
            """
            UPDATE products
            SET name = @Name,
                category_id = @CategoryId,
                brand_id = @BrandId,
                model = @Model,
                barcode = @Barcode,
                wholesale_price = @WholesalePrice,
                retail_price = @RetailPrice,
                sale_price = @SalePrice,
                min_stock_threshold = @MinStockThreshold,
                supplier_id = @SupplierId,
                updated_at_utc = UTC_TIMESTAMP(6)
            WHERE id = @Id;
            """,
            product);
    }

    public async Task DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        // Never hard-deleted: historical invoices reference this row (FR-002).
        await connection.ExecuteAsync(
            "UPDATE products SET is_active = FALSE, updated_at_utc = UTC_TIMESTAMP(6) WHERE id = @id;",
            new { id });
    }

    public async Task SetImagePathAsync(
        long id,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            "UPDATE products SET image_path = @imagePath, updated_at_utc = UTC_TIMESTAMP(6) WHERE id = @id;",
            new { id, imagePath });
    }

    public async Task<bool> BarcodeExistsAsync(
        string barcode,
        long? excludingProductId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM products
            WHERE barcode = @barcode AND (@excludingProductId IS NULL OR id <> @excludingProductId);
            """,
            new { barcode, excludingProductId }) > 0;
    }
}
