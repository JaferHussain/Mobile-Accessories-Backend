using MoizPos.Domain.Enums;

namespace MoizPos.Application.Abstractions;

/// <summary>One row of a customer's ledger, as the screen shows it.</summary>
public sealed record LedgerEntryRow
{
    public long Id { get; init; }

    public DateTime EntryDateUtc { get; init; }

    public LedgerEntryType EntryType { get; init; }

    public long? ReferenceId { get; init; }

    /// <summary>The invoice or receipt number this entry refers to.</summary>
    public string? ReferenceNumber { get; init; }

    public decimal BillAmount { get; init; }

    public decimal PaidAmount { get; init; }

    public decimal BalanceAfter { get; init; }

    /// <summary>
    /// Why this entry exists. Carried by a correction to a carried-forward amount (FR-071),
    /// which has to be visible to whoever reads the ledger — not only in the audit trail.
    /// </summary>
    public string? Note { get; init; }
}

/// <summary>Totals shown on a customer's profile (FR-023).</summary>
public sealed record CustomerSummaryRow
{
    public decimal TotalPurchased { get; init; }

    public decimal TotalPaid { get; init; }

    public decimal TotalOutstanding { get; init; }

    public int InvoiceCount { get; init; }
}

public interface ILedgerRepository
{
    Task<(IReadOnlyList<LedgerEntryRow> Items, int TotalItems)> ListForCustomerAsync(
        long customerId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<CustomerSummaryRow> SummaryForCustomerAsync(
        long customerId, CancellationToken cancellationToken = default);
}

/// <summary>Write side of receiving money from a customer.</summary>
public interface ICustomerPaymentWriteRepository
{
    Task<CustomerBalanceSnapshot?> LockCustomerAsync(
        IUnitOfWork unitOfWork, long customerId, CancellationToken cancellationToken = default);

    Task<long> InsertPaymentAsync(
        IUnitOfWork unitOfWork,
        long customerId,
        string receiptNumber,
        decimal amount,
        PaymentMethod paymentMethod,
        bool isOverpayment,
        string? note,
        long userId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task UpdateBalanceAsync(
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
        string? note = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a customer's carried-forward figure and balance together under a row lock.
    ///
    /// Separate from <see cref="LockCustomerAsync"/> rather than widening the shared snapshot:
    /// the other caller does not select this column, and a property that is silently null on one
    /// code path is the kind of trap this codebase documents rather than creates.
    /// </summary>
    Task<(bool Exists, decimal? OpeningBalance, decimal OutstandingBalance)>
        LockForOpeningBalanceAsync(
            IUnitOfWork unitOfWork, long customerId, CancellationToken cancellationToken = default);

    /// <summary>Writes the carried-forward figure. The balance moves separately, by the difference.</summary>
    Task SetOpeningBalanceAsync(
        IUnitOfWork unitOfWork,
        long customerId,
        decimal openingBalance,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<string> NextReceiptNumberAsync(
        IUnitOfWork unitOfWork, int year, CancellationToken cancellationToken = default);
}
