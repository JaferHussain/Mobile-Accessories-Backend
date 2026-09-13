using MoizPos.Domain.Enums;

namespace MoizPos.Application.Abstractions;

/// <summary>A product's mutable stock and pricing state, read under a row lock.</summary>
public sealed record ProductStockSnapshot
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int QuantityOnHand { get; init; }

    public decimal CostPrice { get; init; }

    public decimal SalePrice { get; init; }
}

/// <summary>A supplier's running payable, read under a row lock.</summary>
public sealed record SupplierBalanceSnapshot
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal PayableBalance { get; init; }
}

/// <summary>
/// The write side of purchasing.
///
/// Every method takes the caller's <see cref="IUnitOfWork"/> so all of them commit or roll back
/// together (Constitution Principle IV). Keeping the SQL here rather than in the service is what
/// keeps Application free of any data-access dependency (Principle II).
/// </summary>
public interface IPurchaseWriteRepository
{
    /// <summary>
    /// Reads a product FOR UPDATE, holding the row until the transaction ends so two concurrent
    /// purchases cannot lose one another's stock increase (research.md R4).
    /// </summary>
    Task<ProductStockSnapshot?> LockProductAsync(
        IUnitOfWork unitOfWork, long productId, CancellationToken cancellationToken = default);

    Task<SupplierBalanceSnapshot?> LockSupplierAsync(
        IUnitOfWork unitOfWork, long supplierId, CancellationToken cancellationToken = default);

    Task<long> InsertPurchaseAsync(
        IUnitOfWork unitOfWork,
        long supplierId,
        long productId,
        DateTime purchaseDateUtc,
        decimal unitCost,
        int quantity,
        decimal total,
        long userId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the new quantity, the overwritten cost, and the sale price in one statement.
    /// </summary>
    Task UpdateProductStockAndPricingAsync(
        IUnitOfWork unitOfWork,
        long productId,
        int newQuantity,
        decimal newCostPrice,
        decimal newSalePrice,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task UpdateSupplierPayableAsync(
        IUnitOfWork unitOfWork,
        long supplierId,
        decimal newPayable,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task InsertSupplierPaymentAsync(
        IUnitOfWork unitOfWork,
        long supplierId,
        decimal amount,
        PaymentMethod paymentMethod,
        bool isOverpayment,
        string? note,
        long userId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);
}
