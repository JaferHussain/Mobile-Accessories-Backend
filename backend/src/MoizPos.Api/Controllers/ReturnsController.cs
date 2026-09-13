using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoizPos.Api.Authorization;
using MoizPos.Application.Contracts.Common;
using MoizPos.Application.Services;

namespace MoizPos.Api.Controllers;

public sealed record SaleReturnLineRequest
{
    public long InvoiceItemId { get; init; }

    public int Quantity { get; init; }
}

public sealed record CreateSaleReturnRequest
{
    public long InvoiceId { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<SaleReturnLineRequest> Items { get; init; } = [];
}

public sealed record CreatePurchaseReturnRequest
{
    public long PurchaseId { get; init; }

    public int Quantity { get; init; }

    public string? Reason { get; init; }
}

public sealed class CreateSaleReturnValidator : AbstractValidator<CreateSaleReturnRequest>
{
    public CreateSaleReturnValidator()
    {
        RuleFor(x => x.InvoiceId).GreaterThan(0).WithMessage("An invoice is required.");

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Select at least one item to return.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.InvoiceItemId).GreaterThan(0);
            item.RuleFor(i => i.Quantity)
                .GreaterThan(0).WithMessage("Return quantity must be greater than zero.");
        });

        RuleFor(x => x.Reason).MaximumLength(255);
    }
}

public sealed class CreatePurchaseReturnValidator : AbstractValidator<CreatePurchaseReturnRequest>
{
    public CreatePurchaseReturnValidator()
    {
        RuleFor(x => x.PurchaseId).GreaterThan(0).WithMessage("A purchase is required.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Return quantity must be greater than zero.");

        RuleFor(x => x.Reason).MaximumLength(255);
    }
}

/// <summary>
/// Sale returns. Staff handle these at the counter — a customer bringing back a faulty charger
/// should not need the owner — and the response reveals no cost or profit (FR-040).
/// </summary>
[ApiController]
[Route("api/sale-returns")]
[Authorize]
public sealed class SaleReturnsController : ControllerBase
{
    private readonly IReturnService _returns;

    public SaleReturnsController(IReturnService returns) => _returns = returns;

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateSaleReturnRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _returns.RecordSaleReturnAsync(
            new RecordSaleReturnRequest
            {
                InvoiceId = request.InvoiceId,
                Reason = request.Reason,
                Items = request.Items
                    .Select(i => new SaleReturnLine { InvoiceItemId = i.InvoiceItemId, Quantity = i.Quantity })
                    .ToList(),
            },
            CurrentUser.Id(User),
            cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created, ApiResponse<RecordSaleReturnResult>.Ok(result));
    }
}

/// <summary>
/// Purchase returns. Admin-only: this route exposes purchase cost and supplier payables.
/// </summary>
[ApiController]
[Route("api/purchase-returns")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class PurchaseReturnsController : ControllerBase
{
    private readonly IReturnService _returns;

    public PurchaseReturnsController(IReturnService returns) => _returns = returns;

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreatePurchaseReturnRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _returns.RecordPurchaseReturnAsync(
            new RecordPurchaseReturnRequest
            {
                PurchaseId = request.PurchaseId,
                Quantity = request.Quantity,
                Reason = request.Reason,
            },
            CurrentUser.Id(User),
            cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created, ApiResponse<RecordPurchaseReturnResult>.Ok(result));
    }
}
