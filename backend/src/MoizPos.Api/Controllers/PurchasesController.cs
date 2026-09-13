using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoizPos.Api.Authorization;
using MoizPos.Application.Abstractions;
using MoizPos.Application.Contracts.Common;
using MoizPos.Application.Services;
using MoizPos.Domain.Entities;

namespace MoizPos.Api.Controllers;

public sealed record CreatePurchaseRequest
{
    public long SupplierId { get; init; }

    public long ProductId { get; init; }

    public decimal UnitCost { get; init; }

    public int Quantity { get; init; }

    public DateTime? PurchaseDate { get; init; }

    /// <summary>
    /// Optional new sale price. Once set it applies to all remaining stock of this product,
    /// including units bought earlier at a lower cost (FR-011d).
    /// </summary>
    public decimal? NewSalePrice { get; init; }
}

public sealed class CreatePurchaseValidator : AbstractValidator<CreatePurchaseRequest>
{
    public CreatePurchaseValidator()
    {
        RuleFor(x => x.SupplierId).GreaterThan(0).WithMessage("A supplier is required.");
        RuleFor(x => x.ProductId).GreaterThan(0).WithMessage("A product is required.");

        RuleFor(x => x.UnitCost)
            .GreaterThan(0).WithMessage("Purchase cost must be greater than zero.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than zero.");

        RuleFor(x => x.NewSalePrice)
            .GreaterThanOrEqualTo(0).When(x => x.NewSalePrice.HasValue)
            .WithMessage("Sale price cannot be negative.");
    }
}

/// <summary>
/// Purchasing. Admin-only: purchase cost is exactly the data Staff must never see (FR-040).
/// </summary>
[ApiController]
[Route("api/purchases")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class PurchasesController : ControllerBase
{
    private readonly IPurchaseService _purchases;
    private readonly IPurchaseRepository _purchaseRepository;

    public PurchasesController(IPurchaseService purchases, IPurchaseRepository purchaseRepository)
    {
        _purchases = purchases;
        _purchaseRepository = purchaseRepository;
    }

    /// <summary>Purchase history, optionally filtered by supplier and date range.</summary>
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] long? supplierId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedSize) =
            PagedResult<Purchase>.Normalize(page, pageSize);

        var (items, total) = await _purchaseRepository.SearchAsync(
            supplierId, from, to, normalizedPage, normalizedSize, cancellationToken);

        return Ok(ApiResponse<PagedResult<Purchase>>.Ok(
            new PagedResult<Purchase>(
                items, normalizedPage, normalizedSize, total)));
    }

    /// <summary>
    /// Records a purchase. One transaction: the purchase row, stock up, the product's cost
    /// overwritten with this purchase's cost, the supplier payable up, a stock movement and the
    /// audit entries (FR-008, FR-011a).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreatePurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _purchases.RecordPurchaseAsync(
            new RecordPurchaseRequest
            {
                SupplierId = request.SupplierId,
                ProductId = request.ProductId,
                UnitCost = request.UnitCost,
                Quantity = request.Quantity,
                PurchaseDateUtc = request.PurchaseDate,
                NewSalePrice = request.NewSalePrice,
            },
            CurrentUser.Id(User),
            cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<RecordPurchaseResult>.Ok(result));
    }
}
