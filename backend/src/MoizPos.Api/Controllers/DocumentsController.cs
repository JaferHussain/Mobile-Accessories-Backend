using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MoizPos.Application.Contracts.Common;
using MoizPos.Application.Services;
using MoizPos.Domain.Enums;

namespace MoizPos.Api.Controllers;

public sealed record ShareLinkRequest
{
    public DocumentType DocumentType { get; init; }

    public long ReferenceId { get; init; }
}

public sealed class ShareLinkValidator : AbstractValidator<ShareLinkRequest>
{
    public ShareLinkValidator() =>
        RuleFor(x => x.ReferenceId).GreaterThan(0).WithMessage("A document is required.");
}

/// <summary>
/// Receipts. Staff may download and share them — handing a customer their receipt is counter
/// work — and nothing here reveals cost or profit (FR-042, FR-043).
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public sealed class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documents;

    public DocumentsController(IDocumentService documents) => _documents = documents;

    [HttpGet("invoices/{id:long}/pdf")]
    public async Task<IActionResult> InvoicePdf(long id, CancellationToken cancellationToken)
    {
        var pdf = await _documents.RenderInvoiceAsync(id, cancellationToken);

        return File(pdf, "application/pdf", $"invoice-{id}.pdf");
    }

    [HttpGet("customer-payments/{id:long}/pdf")]
    public async Task<IActionResult> ReceiptPdf(long id, CancellationToken cancellationToken)
    {
        var pdf = await _documents.RenderReceiptAsync(id, cancellationToken);

        return File(pdf, "application/pdf", $"receipt-{id}.pdf");
    }

    /// <summary>
    /// Mints a share link and the wa.me deep link for it.
    ///
    /// wa.me cannot carry an attachment (research.md R2), so whatsAppUrl contains a link to the
    /// receipt rather than the file. It is null when the customer has no usable mobile number —
    /// the UI then disables the send button and says why (FR-044).
    /// </summary>
    [HttpPost("documents/share-link")]
    public async Task<IActionResult> ShareLink(
        [FromBody] ShareLinkRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _documents.CreateShareLinkAsync(
            request.DocumentType, request.ReferenceId, CurrentUser.Id(User), cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ApiResponse<ShareLinkResult>.Ok(result));
    }
}

/// <summary>
/// THE ONLY UNAUTHENTICATED DATA ENDPOINT IN THE SYSTEM.
///
/// <para>A shop customer holds no credential and never will, and wa.me cannot attach a file, so
/// a receipt reaches them as a link. This is the justified exception recorded in plan.md's
/// Complexity Tracking.</para>
///
/// <para>Containment: a ≥128-bit token resolving to exactly one document, stored only as a hash,
/// expiring, revocable, rate-limited, and access-logged. Unknown, expired and revoked tokens all
/// return an identical 404 so a probe learns nothing.</para>
/// </summary>
[ApiController]
[Route("api/public/documents")]
[AllowAnonymous]
[EnableRateLimiting(PublicDocumentsController.RateLimitPolicy)]
public sealed class PublicDocumentsController : ControllerBase
{
    public const string RateLimitPolicy = "public-documents";

    private readonly IDocumentService _documents;
    private readonly ILogger<PublicDocumentsController> _logger;

    public PublicDocumentsController(
        IDocumentService documents,
        ILogger<PublicDocumentsController> logger)
    {
        _documents = documents;
        _logger = logger;
    }

    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken cancellationToken)
    {
        var resolved = await _documents.ResolveTokenAsync(token, cancellationToken);

        if (resolved is null)
        {
            _logger.LogInformation(
                "Rejected a document token from {RemoteIp}", HttpContext.Connection.RemoteIpAddress);

            return NotFound();
        }

        _logger.LogInformation(
            "Served {DocumentType} {ReferenceId} via share link to {RemoteIp}",
            resolved.DocumentType, resolved.ReferenceId, HttpContext.Connection.RemoteIpAddress);

        var pdf = resolved.DocumentType == DocumentType.Invoice
            ? await _documents.RenderInvoiceAsync(resolved.ReferenceId, cancellationToken)
            : await _documents.RenderReceiptAsync(resolved.ReferenceId, cancellationToken);

        // Inline so it opens in the phone's browser rather than forcing a download.
        Response.Headers.ContentDisposition = "inline; filename=receipt.pdf";

        return File(pdf, "application/pdf");
    }
}
