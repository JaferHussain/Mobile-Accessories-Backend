using MoizPos.Domain.Enums;

namespace MoizPos.Application.Abstractions;

/// <summary>An invoice line as the return needs it, locked for update.</summary>
public sealed record InvoiceItemSnapshot
{
    public long Id { get; init; }

    public long InvoiceId { get; init; }

    public long ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public int ReturnedQty { get; init; }

    public decimal UnitSalePrice { get; init; }

    /// <summary>The cost recorded when the sale was made, not the product's cost today.</summary>
    public decimal UnitCostPrice { get; init; }

    public decimal LineDiscount { get; init; }
}

/// <summary>The invoice being returned against, locked for update.</summary>
public sealed record InvoiceSnapshot
{
    public long Id { get; init; }

    public string InvoiceNumber { get; init; } = string.Empty;

    public long? CustomerId { get; init; }

    public decimal Total { get; init; }

    public decimal AmountPaid { get; init; }

    public decimal AmountRemaining { get; init; }

    public decimal NetAmount { get; init; }
}

/// <summary>A purchase being returned against, locked for update.</summary>
public sealed record PurchaseSnapshot
{
    public long Id { get; init; }

    public long SupplierId { get; init; }

    public long ProductId { get; init; }

    public int Quantity { get; init; }

    public int ReturnedQty { get; init; }

    public decimal UnitCost { get; init; }
}

public sealed record SaleReturnItemToWrite(
    long InvoiceItemId,
    long ProductId,
    string ProductName,
    int Quantity,
    decimal UnitSalePrice,
    decimal UnitCostPrice,
    decimal LineTotal);

public interface IReturnWriteRepository
{
    Task<InvoiceSnapshot?> LockInvoiceAsync(
        IUnitOfWork unitOfWork, long invoiceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InvoiceItemSnapshot>> LockInvoiceItemsAsync(
        IUnitOfWork unitOfWork,
        long invoiceId,
        IReadOnlyList<long> invoiceItemIds,
        CancellationToken cancellationToken = default);

    Task<PurchaseSnapshot?> LockPurchaseAsync(
        IUnitOfWork unitOfWork, long purchaseId, CancellationToken cancellationToken = default);

    Task<long> InsertSaleReturnAsync(
        IUnitOfWork unitOfWork,
        long invoiceId,
        string returnNumber,
        DateTime returnDateUtc,
        decimal totalAmount,
        decimal refundDue,
        string? reason,
        long userId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task InsertSaleReturnItemsAsync(
        IUnitOfWork unitOfWork,
        long saleReturnId,
        IReadOnlyList<SaleReturnItemToWrite> items,
        CancellationToken cancellationToken = default);

    Task IncrementInvoiceItemReturnedAsync(
        IUnitOfWork unitOfWork,
        long invoiceItemId,
        int quantity,
        CancellationToken cancellationToken = default);

    Task ReduceInvoiceNetAmountAsync(
        IUnitOfWork unitOfWork,
        long invoiceId,
        decimal newNetAmount,
        CancellationToken cancellationToken = default);

    Task<long> InsertPurchaseReturnAsync(
        IUnitOfWork unitOfWork,
        long purchaseId,
        long supplierId,
        long productId,
        string returnNumber,
        DateTime returnDateUtc,
        int quantity,
        decimal unitCost,
        decimal total,
        string? reason,
        long userId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task IncrementPurchaseReturnedAsync(
        IUnitOfWork unitOfWork,
        long purchaseId,
        int quantity,
        CancellationToken cancellationToken = default);

    Task<string> NextReturnNumberAsync(
        IUnitOfWork unitOfWork, string prefix, int year, CancellationToken cancellationToken = default);
}
