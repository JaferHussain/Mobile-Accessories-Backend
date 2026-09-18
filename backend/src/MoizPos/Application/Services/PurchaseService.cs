using System.Globalization;
using MoizPos.Application.Abstractions;
using MoizPos.Application.Calculations;
using MoizPos.Application.Time;
using MoizPos.Domain.Enums;
using MoizPos.Domain.Errors;

namespace MoizPos.Application.Services;

public sealed record RecordPurchaseRequest
{
    public long SupplierId { get; init; }

    public long ProductId { get; init; }

    public decimal UnitCost { get; init; }

    public int Quantity { get; init; }

    public DateTime? PurchaseDateUtc { get; init; }

    /// <summary>
    /// Optional. When supplied, the product's sale price is updated and then applies to all
    /// remaining stock, including units bought earlier at a different cost (FR-011d).
    /// </summary>
    public decimal? NewSalePrice { get; init; }
}

public sealed record RecordPurchaseResult(
    long PurchaseId,
    int NewQuantityOnHand,
    decimal NewCostPrice,
    decimal NewSupplierPayable);

public sealed record RecordSupplierPaymentRequest
{
    public long SupplierId { get; init; }

    public decimal Amount { get; init; }

    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Cash;

    public string? Note { get; init; }

    public bool ConfirmOverpayment { get; init; }
}

public interface IPurchaseService
{
    Task<RecordPurchaseResult> RecordPurchaseAsync(
        RecordPurchaseRequest request,
        long userId,
        CancellationToken cancellationToken = default);

    Task<decimal> RecordSupplierPaymentAsync(
        RecordSupplierPaymentRequest request,
        long userId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Purchasing.
///
/// <para><b>The costing rule.</b> The shop owner chose <i>latest purchase cost</i>: when stock is
/// bought, that purchase's unit cost replaces the product's cost for every unit on hand — not a
/// weighted average. Buying 10 at 800, selling 5, then buying 10 at 850 leaves all 15 units
/// costed at 850, not 825 (FR-011a). Sales already recorded keep the cost they snapshotted, so
/// history is never rewritten (FR-011c).</para>
///
/// <para>Recording a purchase is one transaction doing six things (data-model.md §11). All of
/// it, or none of it.</para>
/// </summary>
public sealed class PurchaseService : IPurchaseService
{
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IPurchaseWriteRepository _purchases;
    private readonly IStockMovementWriter _stockMovements;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public PurchaseService(
        IUnitOfWorkFactory unitOfWorkFactory,
        IPurchaseWriteRepository purchases,
        IStockMovementWriter stockMovements,
        IAuditWriter audit,
        IClock clock)
    {
        _unitOfWorkFactory = unitOfWorkFactory;
        _purchases = purchases;
        _stockMovements = stockMovements;
        _audit = audit;
        _clock = clock;
    }

    public async Task<RecordPurchaseResult> RecordPurchaseAsync(
        RecordPurchaseRequest request,
        long userId,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            throw new BusinessRuleViolationException("Purchase quantity must be greater than zero.");
        }

        if (request.UnitCost <= 0m)
        {
            throw new BusinessRuleViolationException("Purchase unit cost must be greater than zero.");
        }

        if (request.NewSalePrice is < 0m)
        {
            throw new BusinessRuleViolationException("Sale price cannot be negative.");
        }

        var nowUtc = _clock.UtcNow;
        var purchaseDate = request.PurchaseDateUtc ?? nowUtc;
        var total = Round(request.UnitCost * request.Quantity);

        await using var uow = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        // Locked in a fixed order (product then supplier) so concurrent purchases cannot deadlock.
        var product = await _purchases.LockProductAsync(uow, request.ProductId, cancellationToken)
            ?? throw new NotFoundException("Product", request.ProductId);

        var supplier = await _purchases.LockSupplierAsync(uow, request.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Supplier", request.SupplierId);

        // 1. The purchase itself.
        var purchaseId = await _purchases.InsertPurchaseAsync(
            uow, request.SupplierId, request.ProductId, purchaseDate,
            request.UnitCost, request.Quantity, total, userId, nowUtc, cancellationToken);

        // 2 & 3. Stock up; cost overwritten for ALL units on hand.
        var newQuantity = StockRules.NextQuantity(product.QuantityOnHand, request.Quantity, product.Name);
        var newCost = StockRules.NextCostPrice(product.CostPrice, request.UnitCost);
        var newSalePrice = request.NewSalePrice ?? product.SalePrice;

        await _purchases.UpdateProductStockAndPricingAsync(
            uow, request.ProductId, newQuantity, newCost, newSalePrice, nowUtc, cancellationToken);

        // 4. What the shop now owes.
        var newPayable = Round(supplier.PayableBalance + total);

        await _purchases.UpdateSupplierPayableAsync(
            uow, request.SupplierId, newPayable, nowUtc, cancellationToken);

        // 5. The movement explaining the quantity change.
        await _stockMovements.AppendAsync(
            uow, request.ProductId, request.Quantity, newQuantity,
            StockMovementReason.Purchase, purchaseId, userId,
            note: null, nowUtc, cancellationToken);

        // 6. Audit every value that moved.
        await AuditAsync(uow, "Product", request.ProductId, "quantity_on_hand",
            product.QuantityOnHand, newQuantity, "Purchase", userId, nowUtc, cancellationToken);

        await AuditAsync(uow, "Product", request.ProductId, "cost_price",
            product.CostPrice, newCost, "Purchase", userId, nowUtc, cancellationToken);

        await AuditAsync(uow, "Supplier", request.SupplierId, "payable_balance",
            supplier.PayableBalance, newPayable, "Purchase", userId, nowUtc, cancellationToken);

        if (newSalePrice != product.SalePrice)
        {
            await AuditAsync(uow, "Product", request.ProductId, "sale_price",
                product.SalePrice, newSalePrice, "Purchase", userId, nowUtc, cancellationToken);
        }

        await uow.CommitAsync(cancellationToken);

        return new RecordPurchaseResult(purchaseId, newQuantity, newCost, newPayable);
    }

    public async Task<decimal> RecordSupplierPaymentAsync(
        RecordSupplierPaymentRequest request,
        long userId,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0m)
        {
            throw new BusinessRuleViolationException("Payment amount must be greater than zero.");
        }

        var nowUtc = _clock.UtcNow;

        await using var uow = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        var supplier = await _purchases.LockSupplierAsync(uow, request.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Supplier", request.SupplierId);

        // Paying more than is owed must be deliberate, never an accident (FR-009).
        if (request.Amount > supplier.PayableBalance && !request.ConfirmOverpayment)
        {
            throw new OverpaymentNotConfirmedException(request.Amount, supplier.PayableBalance);
        }

        var newPayable = Round(supplier.PayableBalance - request.Amount);

        await _purchases.InsertSupplierPaymentAsync(
            uow, request.SupplierId, request.Amount, request.PaymentMethod,
            isOverpayment: newPayable < 0m, request.Note, userId, nowUtc, cancellationToken);

        await _purchases.UpdateSupplierPayableAsync(
            uow, request.SupplierId, newPayable, nowUtc, cancellationToken);

        await AuditAsync(uow, "Supplier", request.SupplierId, "payable_balance",
            supplier.PayableBalance, newPayable, "SupplierPayment", userId, nowUtc, cancellationToken);

        await uow.CommitAsync(cancellationToken);

        return newPayable;
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
