using System.Globalization;
using MoizPos.Application.Abstractions;
using MoizPos.Application.Calculations;
using MoizPos.Application.Time;
using MoizPos.Domain.Enums;
using MoizPos.Domain.Errors;

namespace MoizPos.Application.Services;

public sealed record SaleReturnLine
{
    public long InvoiceItemId { get; init; }

    public int Quantity { get; init; }
}

public sealed record RecordSaleReturnRequest
{
    public long InvoiceId { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<SaleReturnLine> Items { get; init; } = [];
}

public sealed record RecordSaleReturnResult(
    long ReturnId,
    string ReturnNumber,
    decimal TotalReturned,
    decimal RefundDue,
    decimal? CustomerBalance);

public sealed record RecordPurchaseReturnRequest
{
    public long PurchaseId { get; init; }

    public int Quantity { get; init; }

    public string? Reason { get; init; }
}

public sealed record RecordPurchaseReturnResult(
    long ReturnId,
    string ReturnNumber,
    decimal TotalReturned,
    int NewQuantityOnHand,
    decimal NewSupplierPayable);

public interface IReturnService
{
    Task<RecordSaleReturnResult> RecordSaleReturnAsync(
        RecordSaleReturnRequest request, long userId, CancellationToken cancellationToken = default);

    Task<RecordPurchaseReturnResult> RecordPurchaseReturnAsync(
        RecordPurchaseReturnRequest request, long userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Returns — the inverse of a sale or a purchase.
///
/// <para><b>Reversal is at the recorded values, never today's.</b> Each invoice line carries the
/// sale price and the cost snapshotted when it was sold; each purchase carries what it actually
/// cost. Under the shop's latest-cost rule the product's current cost moves with every purchase,
/// so reversing at today's figure would leave a residue in profit exactly equal to the drift
/// (research.md R11).</para>
///
/// <para>A purchase return deliberately does NOT revert products.cost_price. If a purchase was
/// entered at the wrong cost and then returned, the owner corrects it with a stock/price
/// adjustment — a documented consequence of the latest-cost model.</para>
/// </summary>
public sealed class ReturnService : IReturnService
{
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IReturnWriteRepository _returns;
    private readonly IInvoiceWriteRepository _invoices;
    private readonly IPurchaseWriteRepository _purchases;
    private readonly IStockWriteRepository _stock;
    private readonly IStockMovementWriter _stockMovements;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public ReturnService(
        IUnitOfWorkFactory unitOfWorkFactory,
        IReturnWriteRepository returns,
        IInvoiceWriteRepository invoices,
        IPurchaseWriteRepository purchases,
        IStockWriteRepository stock,
        IStockMovementWriter stockMovements,
        IAuditWriter audit,
        IClock clock)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _returns = returns;
        _invoices = invoices;
        _purchases = purchases;
        _stock = stock;
        _stockMovements = stockMovements;
        _audit = audit;
        _clock = clock;
    }

    public async Task<RecordSaleReturnResult> RecordSaleReturnAsync(
        RecordSaleReturnRequest request,
        long userId,
        CancellationToken cancellationToken = default)
    {
        if (request.Items.Count == 0)
        {
            throw new BusinessRuleViolationException("A return must include at least one item.");
        }

        var nowUtc = _clock.UtcNow;

        await using var uow = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        var invoice = await _returns.LockInvoiceAsync(uow, request.InvoiceId, cancellationToken)
            ?? throw new NotFoundException("Invoice", request.InvoiceId);

        var itemIds = request.Items.Select(i => i.InvoiceItemId).OrderBy(id => id).ToList();
        var lines = await _returns.LockInvoiceItemsAsync(uow, request.InvoiceId, itemIds, cancellationToken);
        var byId = lines.ToDictionary(l => l.Id);

        var toWrite = new List<SaleReturnItemToWrite>(request.Items.Count);
        var totalReturned = 0m;

        foreach (var requested in request.Items)
        {
            if (!byId.TryGetValue(requested.InvoiceItemId, out var line))
            {
                throw new NotFoundException("Invoice item", requested.InvoiceItemId);
            }

            // Refuses more than was sold, including what earlier returns already took (FR-026).
            StockRules.EnsureReturnable(line.Quantity, line.ReturnedQty, requested.Quantity);

            var lineTotal = Round(line.UnitSalePrice * requested.Quantity);
            totalReturned += lineTotal;

            toWrite.Add(new SaleReturnItemToWrite(
                line.Id, line.ProductId, line.ProductName, requested.Quantity,
                line.UnitSalePrice, line.UnitCostPrice, lineTotal));
        }

        totalReturned = Round(totalReturned);

        // A return can never give back more than the invoice is still worth.
        if (totalReturned > invoice.NetAmount)
        {
            throw new BusinessRuleViolationException(
                $"Returning {totalReturned:0.00} exceeds the invoice's remaining value of {invoice.NetAmount:0.00}.");
        }

        // Where the money goes depends on whether the customer already paid. An unpaid credit
        // sale reduces what they owe; a settled sale creates a refund the shop owes them
        // (FR-024, FR-028) — it must never be silently discarded.
        var reducesBalance = Math.Min(totalReturned, invoice.AmountRemaining);
        var refundDue = Round(totalReturned - reducesBalance);

        var returnNumber = await _returns.NextReturnNumberAsync(uow, "SRT", nowUtc.Year, cancellationToken);

        var returnId = await _returns.InsertSaleReturnAsync(
            uow, request.InvoiceId, returnNumber, nowUtc, totalReturned, refundDue,
            request.Reason, userId, nowUtc, cancellationToken);

        await _returns.InsertSaleReturnItemsAsync(uow, returnId, toWrite, cancellationToken);

        foreach (var item in toWrite)
        {
            await _returns.IncrementInvoiceItemReturnedAsync(
                uow, item.InvoiceItemId, item.Quantity, cancellationToken);

            var product = await _stock.LockProductAsync(uow, item.ProductId, cancellationToken)
                ?? throw new NotFoundException("Product", item.ProductId);

            var newQuantity = StockRules.NextQuantity(
                product.QuantityOnHand, item.Quantity, product.Name);

            await _stock.UpdateQuantityAsync(uow, item.ProductId, newQuantity, nowUtc, cancellationToken);

            await _stockMovements.AppendAsync(
                uow, item.ProductId, item.Quantity, newQuantity, StockMovementReason.SaleReturn,
                returnId, userId, request.Reason, nowUtc, cancellationToken);

            await AuditAsync(uow, "Product", item.ProductId, "quantity_on_hand",
                product.QuantityOnHand, newQuantity, "SaleReturn", userId, nowUtc, cancellationToken);
        }

        await _returns.ReduceInvoiceNetAmountAsync(
            uow, request.InvoiceId, Round(invoice.NetAmount - totalReturned), cancellationToken);

        decimal? customerBalance = null;

        if (invoice.CustomerId is not null && reducesBalance > 0m)
        {
            var customer = await _invoices.LockCustomerAsync(uow, invoice.CustomerId.Value, cancellationToken)
                ?? throw new NotFoundException("Customer", invoice.CustomerId.Value);

            var newBalance = Round(customer.OutstandingBalance - reducesBalance);

            await _invoices.UpdateCustomerBalanceAsync(
                uow, invoice.CustomerId.Value, newBalance, nowUtc, cancellationToken);

            await _invoices.InsertLedgerEntryAsync(
                uow, invoice.CustomerId.Value, nowUtc, LedgerEntryType.SaleReturn, returnId,
                billAmount: 0m, paidAmount: reducesBalance, balanceAfter: newBalance,
                userId, nowUtc, cancellationToken);

            await AuditAsync(uow, "Customer", invoice.CustomerId.Value, "outstanding_balance",
                customer.OutstandingBalance, newBalance, "SaleReturn", userId, nowUtc, cancellationToken);

            customerBalance = newBalance;
        }

        await uow.CommitAsync(cancellationToken);

        return new RecordSaleReturnResult(
            returnId, returnNumber, totalReturned, refundDue, customerBalance);
    }

    public async Task<RecordPurchaseReturnResult> RecordPurchaseReturnAsync(
        RecordPurchaseReturnRequest request,
        long userId,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            throw new BusinessRuleViolationException("Return quantity must be greater than zero.");
        }

        var nowUtc = _clock.UtcNow;

        await using var uow = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        var purchase = await _returns.LockPurchaseAsync(uow, request.PurchaseId, cancellationToken)
            ?? throw new NotFoundException("Purchase", request.PurchaseId);

        StockRules.EnsureReturnable(purchase.Quantity, purchase.ReturnedQty, request.Quantity);

        var product = await _stock.LockProductAsync(uow, purchase.ProductId, cancellationToken)
            ?? throw new NotFoundException("Product", purchase.ProductId);

        var supplier = await _purchases.LockSupplierAsync(uow, purchase.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Supplier", purchase.SupplierId);

        // Guarded: goods already sold cannot be sent back to the supplier (FR-025).
        var newQuantity = StockRules.NextQuantity(
            product.QuantityOnHand, -request.Quantity, product.Name);

        // At the ORIGINAL purchase cost, so the payable unwinds by exactly what it added.
        var total = Round(purchase.UnitCost * request.Quantity);
        var newPayable = Round(supplier.PayableBalance - total);

        var returnNumber = await _returns.NextReturnNumberAsync(uow, "PRT", nowUtc.Year, cancellationToken);

        var returnId = await _returns.InsertPurchaseReturnAsync(
            uow, request.PurchaseId, purchase.SupplierId, purchase.ProductId, returnNumber,
            nowUtc, request.Quantity, purchase.UnitCost, total, request.Reason,
            userId, nowUtc, cancellationToken);

        await _returns.IncrementPurchaseReturnedAsync(
            uow, request.PurchaseId, request.Quantity, cancellationToken);

        await _stock.UpdateQuantityAsync(uow, purchase.ProductId, newQuantity, nowUtc, cancellationToken);

        await _purchases.UpdateSupplierPayableAsync(
            uow, purchase.SupplierId, newPayable, nowUtc, cancellationToken);

        await _stockMovements.AppendAsync(
            uow, purchase.ProductId, -request.Quantity, newQuantity,
            StockMovementReason.PurchaseReturn, returnId, userId, request.Reason,
            nowUtc, cancellationToken);

        await AuditAsync(uow, "Product", purchase.ProductId, "quantity_on_hand",
            product.QuantityOnHand, newQuantity, "PurchaseReturn", userId, nowUtc, cancellationToken);

        await AuditAsync(uow, "Supplier", purchase.SupplierId, "payable_balance",
            supplier.PayableBalance, newPayable, "PurchaseReturn", userId, nowUtc, cancellationToken);

        await uow.CommitAsync(cancellationToken);

        return new RecordPurchaseReturnResult(
            returnId, returnNumber, total, newQuantity, newPayable);
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private Task AuditAsync<T>(
        IUnitOfWork uow, string entityType, long entityId, string field,
        T oldValue, T newValue, string action, long userId, DateTime nowUtc,
        CancellationToken cancellationToken) =>
        _audit.RecordAsync(
            uow, entityType, entityId, field,
            Convert.ToString(oldValue, CultureInfo.InvariantCulture),
            Convert.ToString(newValue, CultureInfo.InvariantCulture),
            action, userId, nowUtc, cancellationToken);
}
