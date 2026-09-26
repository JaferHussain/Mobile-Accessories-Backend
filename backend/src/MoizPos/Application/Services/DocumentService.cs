using System.Security.Cryptography;
using System.Text;
using MoizPos.Application.Abstractions;
using MoizPos.Application.Documents;
using MoizPos.Application.Time;
using MoizPos.Domain.Enums;
using MoizPos.Domain.Errors;

namespace MoizPos.Application.Services;

/// <param name="WhatsAppUrl">Null when there is no usable number, same as <paramref name="SmsUrl"/>.</param>
/// <param name="SmsUrl">The same link through the device's own SMS app. Both are deep links —
/// nothing is sent by the server, so the shop needs no messaging account and pays nothing per
/// message.</param>
public sealed record ShareLinkResult(
    string ShareUrl,
    string? WhatsAppUrl,
    string? SmsUrl,
    DateTime ExpiresAtUtc);

public sealed record ResolvedDocument(DocumentType DocumentType, long ReferenceId);

public interface IDocumentService
{
    Task<byte[]> RenderInvoiceAsync(long invoiceId, CancellationToken cancellationToken = default);

    Task<byte[]> RenderReceiptAsync(long paymentId, CancellationToken cancellationToken = default);

    /// <param name="suppliedMobileNumber">
    /// A number typed at the counter for a WALK-IN — a sale with no customer, which is most
    /// counter sales. Used only when the document has no number on file, and never stored.
    /// </param>
    Task<ShareLinkResult> CreateShareLinkAsync(
        DocumentType documentType, long referenceId, long userId,
        string? suppliedMobileNumber = null,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves a raw token to its document, or null for unknown/expired/revoked.</summary>
    Task<ResolvedDocument?> ResolveTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>What is outstanding for one document. Revoking what you cannot see is not an
    /// action anyone can take, so the listing exists for the revoke to be usable.</summary>
    Task<IReadOnlyList<ShareLinkSummary>> ListShareLinksAsync(
        DocumentType documentType, long referenceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws one link. Idempotent: revoking an already-revoked link returns the ORIGINAL
    /// revocation time untouched, because that timestamp is evidence of when the withdrawal
    /// actually happened. Null when no such link exists.
    /// </summary>
    Task<ShareLinkSummary?> RevokeShareLinkAsync(
        long shareLinkId, CancellationToken cancellationToken = default);
}

public sealed class DocumentOptions
{
    /// <summary>Base URL a customer's phone can reach, e.g. https://shop.example.</summary>
    public string PublicBaseUrl { get; init; } = string.Empty;

    public int ShareLinkExpiryDays { get; init; } = 30;

    public ShopDetails Shop { get; init; } = new();
}

/// <summary>
/// Produces receipts and the links that carry them to a customer's phone.
///
/// <para><b>Why a link and not a file.</b> <c>wa.me</c> cannot attach a document
/// (research.md R2), so the message carries a URL. That URL must work for someone who holds no
/// credential, which is the single justified exception to per-endpoint authentication in this
/// system (plan.md Complexity Tracking).</para>
///
/// <para>The raw token exists only in the message. Only its SHA-256 hash is stored, so a
/// database leak does not hand over live receipt links.</para>
/// </summary>
public sealed class DocumentService : IDocumentService
{
    /// <summary>256 bits — comfortably past the ≥128 the plan requires.</summary>
    private const int TokenBytes = 32;

    private readonly IInvoiceReadRepository _invoices;
    private readonly ICustomerRepository _customers;
    private readonly ICustomerPaymentReadRepository _payments;
    private readonly IDocumentTokenRepository _tokens;
    private readonly IPdfRendererPort _renderer;
    private readonly PeriodResolver _periods;
    private readonly IClock _clock;
    private readonly DocumentOptions _options;

    public DocumentService(
        IInvoiceReadRepository invoices,
        ICustomerRepository customers,
        ICustomerPaymentReadRepository payments,
        IDocumentTokenRepository tokens,
        IPdfRendererPort renderer,
        PeriodResolver periods,
        IClock clock,
        DocumentOptions options)
    {
        _invoices = invoices;
        _customers = customers;
        _payments = payments;
        _tokens = tokens;
        _renderer = renderer;
        _periods = periods;
        _clock = clock;
        _options = options;
    }

    public async Task<byte[]> RenderInvoiceAsync(
        long invoiceId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _invoices.FindByIdAsync(invoiceId, cancellationToken)
            ?? throw new NotFoundException("Invoice", invoiceId);

        var customer = invoice.Invoice.CustomerId is null
            ? null
            : await _customers.FindByIdAsync(invoice.Invoice.CustomerId.Value, cancellationToken);

        var document = DocumentAssembler.BuildInvoice(
            _options.Shop, invoice.Invoice, invoice.Items,
            customer?.Name ?? invoice.CustomerName, customer?.MobileNumber, _periods);

        return _renderer.RenderInvoice(document);
    }

    public async Task<byte[]> RenderReceiptAsync(
        long paymentId,
        CancellationToken cancellationToken = default)
    {
        var payment = await _payments.FindByIdAsync(paymentId, cancellationToken)
            ?? throw new NotFoundException("Payment", paymentId);

        var customer = await _customers.FindByIdAsync(payment.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer", payment.CustomerId);

        var document = DocumentAssembler.BuildReceipt(
            _options.Shop, payment, customer.Name, customer.MobileNumber,
            customer.OutstandingBalance, _periods);

        return _renderer.RenderReceipt(document);
    }

    public async Task<ShareLinkResult> CreateShareLinkAsync(
        DocumentType documentType,
        long referenceId,
        long userId,
        string? suppliedMobileNumber = null,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var (mobileNumber, message) = documentType switch
        {
            DocumentType.Invoice => await InvoiceMessageAsync(referenceId, cancellationToken),
            DocumentType.PaymentReceipt => await ReceiptMessageAsync(referenceId, cancellationToken),
            _ => throw new BusinessRuleViolationException("Unknown document type."),
        };

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var expiresAt = nowUtc.AddDays(_options.ShareLinkExpiryDays);

        await _tokens.CreateAsync(
            Hash(token), documentType, referenceId, expiresAt, userId, nowUtc, cancellationToken);

        var shareUrl = $"{_options.PublicBaseUrl.TrimEnd('/')}/api/public/documents/{token}";

        // A number on file wins. It is the shop's own record of where a bill was sent, and a
        // number typed at the counter must never silently redirect a known customer's bill
        // (FR-124). The supplied one is for a walk-in, who has no record to read (FR-123) — and
        // it is used here and nowhere else: nothing is written back.
        var sendTo = string.IsNullOrWhiteSpace(mobileNumber) ? suppliedMobileNumber : mobileNumber;

        var whatsApp = WhatsAppLinkBuilder.Build(sendTo, message, shareUrl);
        var sms = SmsLinkBuilder.Build(sendTo, message, shareUrl);

        // A missing or unusable number is not an error: the document still has a link the
        // shopkeeper can copy. The UI disables both send buttons and says why (FR-044, FR-121).
        return new ShareLinkResult(shareUrl, whatsApp?.Url, sms, expiresAt);
    }

    public async Task<ResolvedDocument?> ResolveTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var stored = await _tokens.FindAsync(Hash(token), cancellationToken);
        var nowUtc = _clock.UtcNow;

        // Unknown, expired and revoked are all reported identically by the caller, so a probe
        // cannot learn whether a token ever existed.
        if (stored is null || !stored.IsUsable(nowUtc))
        {
            return null;
        }

        await _tokens.RecordAccessAsync(stored.Id, nowUtc, cancellationToken);

        return new ResolvedDocument(stored.DocumentType, stored.ReferenceId);
    }

    private async Task<(string? Mobile, string Message)> InvoiceMessageAsync(
        long invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.FindByIdAsync(invoiceId, cancellationToken)
            ?? throw new NotFoundException("Invoice", invoiceId);

        var customer = invoice.Invoice.CustomerId is null
            ? null
            : await _customers.FindByIdAsync(invoice.Invoice.CustomerId.Value, cancellationToken);

        return (
            customer?.MobileNumber,
            DocumentMessages.Invoice(
                _options.Shop,
                invoice.Invoice.InvoiceNumber,
                customer?.Name,
                invoice.Invoice.Total,
                invoice.Invoice.AmountRemaining));
    }

    private async Task<(string? Mobile, string Message)> ReceiptMessageAsync(
        long paymentId, CancellationToken cancellationToken)
    {
        var payment = await _payments.FindByIdAsync(paymentId, cancellationToken)
            ?? throw new NotFoundException("Payment", paymentId);

        var customer = await _customers.FindByIdAsync(payment.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer", payment.CustomerId);

        // The balance AT THIS PAYMENT, from the ledger entry it wrote — not the customer's
        // balance today, which has already moved on if they have bought again since.
        var balanceAfter = await _payments.BalanceAfterPaymentAsync(paymentId, cancellationToken)
                           ?? customer.OutstandingBalance;

        return (
            customer.MobileNumber,
            DocumentMessages.PaymentReceipt(
                _options.Shop, payment.ReceiptNumber, customer.Name, payment.Amount, balanceAfter));
    }

    public Task<IReadOnlyList<ShareLinkSummary>> ListShareLinksAsync(
        DocumentType documentType,
        long referenceId,
        CancellationToken cancellationToken = default) =>
        _tokens.ListForDocumentAsync(documentType, referenceId, _clock.UtcNow, cancellationToken);

    public async Task<ShareLinkSummary?> RevokeShareLinkAsync(
        long shareLinkId,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var existing = await _tokens.FindByIdAsync(shareLinkId, nowUtc, cancellationToken);

        if (existing is null)
        {
            return null;
        }

        // RevokeAsync only writes where revoked_at_utc IS NULL, so a second call cannot move the
        // original timestamp. Re-read rather than assume, so what is returned is what is stored.
        await _tokens.RevokeAsync(shareLinkId, nowUtc, cancellationToken);

        return await _tokens.FindByIdAsync(shareLinkId, nowUtc, cancellationToken);
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

/// <summary>Rendering port, so Application stays free of the PDF library (Constitution II).</summary>
public interface IPdfRendererPort
{
    byte[] RenderInvoice(InvoiceDocument document);

    byte[] RenderReceipt(ReceiptDocument document);
}

/// <summary>Reads a single customer payment for its receipt.</summary>
public interface ICustomerPaymentReadRepository
{
    Task<Domain.Entities.CustomerPayment?> FindByIdAsync(
        long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The balance recorded on the ledger entry for this payment — what the customer owed once it
    /// was applied.
    ///
    /// <para>Deliberately NOT the customer's balance today. The two part company the moment the
    /// customer buys again, and a receipt that restates itself afterwards contradicts the shop's
    /// own register. Null when no ledger entry references this payment, which should not happen
    /// and is treated as "cannot say" rather than "zero".</para>
    /// </summary>
    Task<decimal?> BalanceAfterPaymentAsync(
        long paymentId, CancellationToken cancellationToken = default);
}
