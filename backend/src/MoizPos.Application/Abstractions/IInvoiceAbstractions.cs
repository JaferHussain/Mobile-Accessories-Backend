using MoizPos.Domain.Entities;
using MoizPos.Domain.Enums;

namespace MoizPos.Application.Abstractions;

/// <summary>A customer's balance, read under a row lock.</summary>
public sealed record CustomerBalanceSnapshot
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal OutstandingBalance { get; init; }
}

/// <summary>One line as it will be written, after the server has priced it.</summary>
public sealed record InvoiceItemToWrite(
    long ProductId,
    string ProductName,
    int Quantity,
    decimal UnitSalePrice,
    decimal LineDiscount,
    decimal UnitCostPrice,
    decimal LineTotal);

/// <summary>
/// The write side of selling. Every method takes the caller's transaction so the invoice, the
/// stock decrements and the ledger entry commit together or not at all (Principle IV).
/// </summary>
public interface IInvoiceWriteRepository
{
    /// <summary>
    /// Locks several products at once, ordered by id. A fixed lock order is what stops two
    /// multi-line sales that share products from deadlocking (research.md R4).
    /// </summary>
    Task<IReadOnlyList<ProductStockSnapshot>> LockProductsAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyList<long> productIds,
        CancellationToken cancellationToken = default);

    Task<CustomerBalanceSnapshot?> LockCustomerAsync(
        IUnitOfWork unitOfWork, long customerId, CancellationToken cancellationToken = default);

    Task<long> InsertInvoiceAsync(
        IUnitOfWork unitOfWork,
        string invoiceNumber,
        long? customerId,
        DateTime invoiceDateUtc,
        SaleType saleType,
        decimal subtotal,
        decimal orderDiscount,
        decimal total,
        decimal amountPaid,
        decimal amountRemaining,
        PaymentMethod paymentMethod,
        string? idempotencyKey,
        long userId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task InsertInvoiceItemsAsync(
        IUnitOfWork unitOfWork,
        long invoiceId,
        IReadOnlyList<InvoiceItemToWrite> items,
        CancellationToken cancellationToken = default);

    Task UpdateProductQuantityAsync(
        IUnitOfWork unitOfWork,
        long productId,
        int newQuantity,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task UpdateCustomerBalanceAsync(
        IUnitOfWork unitOfWork,
        long customerId,
        decimal newBalance,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task InsertLedgerEntryAsync(
        IUnitOfWork unitOfWork,
        long customerId,
        DateTime entryDateUtc,
        LedgerEntryType entryType,
        long? referenceId,
        decimal billAmount,
        decimal paidAmount,
        decimal balanceAfter,
        long userId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the existing invoice id for a replayed idempotency key, if any.</summary>
    Task<long?> FindByIdempotencyKeyAsync(
        IUnitOfWork unitOfWork, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Reserves the next invoice number for the year, inside the transaction.</summary>
    Task<string> NextInvoiceNumberAsync(
        IUnitOfWork unitOfWork, int year, CancellationToken cancellationToken = default);
}

/// <summary>An invoice with its lines, for display.</summary>
public sealed record InvoiceWithItems(Invoice Invoice, IReadOnlyList<InvoiceItem> Items, string? CustomerName);

public interface IInvoiceReadRepository
{
    Task<InvoiceWithItems?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Invoice> Items, int TotalItems)> SearchAsync(
        long? customerId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

/// <summary>Customer reads and writes.</summary>
public interface ICustomerRepository
{
    Task<(IReadOnlyList<Customer> Items, int TotalItems)> SearchAsync(
        string? search,
        bool withBalanceOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<Customer?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<long> CreateAsync(Customer customer, CancellationToken cancellationToken = default);

    Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default);
}
