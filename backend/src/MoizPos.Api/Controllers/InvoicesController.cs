using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoizPos.Application.Abstractions;
using MoizPos.Application.Contracts.Common;
using MoizPos.Application.Services;
using MoizPos.Domain.Entities;
using MoizPos.Domain.Enums;
using MoizPos.Domain.Errors;

namespace MoizPos.Api.Controllers;

public sealed record InvoiceLineRequest
{
    public long ProductId { get; init; }

    public int Quantity { get; init; }

    public decimal UnitSalePrice { get; init; }

    public decimal LineDiscount { get; init; }
}

public sealed record NewCustomerRequest
{
    public string Name { get; init; } = string.Empty;

    public string? MobileNumber { get; init; }
}

public sealed record CreateInvoiceApiRequest
{
    public long? CustomerId { get; init; }

    public NewCustomerRequest? NewCustomer { get; init; }

    public decimal OrderDiscount { get; init; }

    public decimal AmountPaid { get; init; }

    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Cash;

    /// <summary>Counter sale or bulk sale. Retail unless the salesman says otherwise.</summary>
    public SaleType SaleType { get; init; } = SaleType.Retail;

    public IReadOnlyList<InvoiceLineRequest> Items { get; init; } = [];
}

public sealed class CreateInvoiceValidator : AbstractValidator<CreateInvoiceApiRequest>
{
    public CreateInvoiceValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Add at least one item before saving the sale.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).GreaterThan(0).WithMessage("A product is required.");

            item.RuleFor(i => i.Quantity)
                .GreaterThan(0).WithMessage("Quantity must be greater than zero.");

            item.RuleFor(i => i.UnitSalePrice)
                .GreaterThanOrEqualTo(0).WithMessage("Unit price cannot be negative.");

            item.RuleFor(i => i.LineDiscount)
                .GreaterThanOrEqualTo(0).WithMessage("Line discount cannot be negative.");
        });

        RuleFor(x => x.OrderDiscount)
            .GreaterThanOrEqualTo(0).WithMessage("Order discount cannot be negative.");

        RuleFor(x => x.AmountPaid)
            .GreaterThanOrEqualTo(0).WithMessage("Amount paid cannot be negative.");

        RuleFor(x => x.NewCustomer!.Name)
            .NotEmpty().When(x => x.NewCustomer is not null)
            .WithMessage("A customer name is required.");
    }
}

/// <summary>
/// Selling. Staff may create and read sales — that is their job — but the responses carry no
/// cost or profit data (FR-040).
/// </summary>
[ApiController]
[Route("api/invoices")]
[Authorize]
public sealed class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _invoices;
    private readonly IInvoiceReadRepository _reads;

    public InvoicesController(IInvoiceService invoices, IInvoiceReadRepository reads)
    {
        _invoices = invoices;
        _reads = reads;
    }

    /// <summary>
    /// Records a sale. One transaction: lock the products, verify stock, price the sale
    /// server-side, write the invoice and lines with the cost snapshotted, decrement stock, and
    /// raise the customer's balance if anything is owed (FR-015).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateInvoiceApiRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _invoices.CreateAsync(
                new CreateInvoiceRequest
                {
                    CustomerId = request.CustomerId,
                    NewCustomer = request.NewCustomer is null
                        ? null
                        : new NewCustomer(request.NewCustomer.Name, request.NewCustomer.MobileNumber),
                    OrderDiscount = request.OrderDiscount,
                    AmountPaid = request.AmountPaid,
                    PaymentMethod = request.PaymentMethod,
                    SaleType = request.SaleType,
                    IdempotencyKey = idempotencyKey,
                    Items = request.Items.Select(i => new CreateInvoiceLine
                    {
                        ProductId = i.ProductId,
                        Quantity = i.Quantity,
                        UnitSalePrice = i.UnitSalePrice,
                        LineDiscount = i.LineDiscount,
                    }).ToList(),
                },
                CurrentUser.Id(User),
                CurrentUser.Role(User),
                cancellationToken);

            return CreatedAtAction(
                nameof(Get), new { id = result.InvoiceId }, ApiResponse<CreateInvoiceResult>.Ok(result));
        }
        catch (DuplicateInvoiceException duplicate)
        {
            // A double-tap or a retried request: hand back the sale already recorded rather than
            // creating a second one or reporting an error the shopkeeper cannot act on.
            var existing = await _reads.FindByIdAsync(duplicate.ExistingInvoiceId, cancellationToken);

            if (existing is null)
            {
                // The key matched an invoice we can no longer read — report the duplicate rather
                // than pretending the retry succeeded.
                throw new BusinessRuleViolationException(duplicate.Message);
            }

            return Ok(ApiResponse<InvoiceWithItems>.Ok(existing));
        }
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        var invoice = await _reads.FindByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Invoice", id);

        return Ok(ApiResponse<InvoiceWithItems>.Ok(invoice));
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] long? customerId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedSize) = PagedResult<Invoice>.Normalize(page, pageSize);

        var (items, total) = await _reads.SearchAsync(
            customerId, from, to, normalizedPage, normalizedSize, cancellationToken);

        return Ok(ApiResponse<PagedResult<Invoice>>.Ok(
            new PagedResult<Invoice>(items, normalizedPage, normalizedSize, total)));
    }
}
