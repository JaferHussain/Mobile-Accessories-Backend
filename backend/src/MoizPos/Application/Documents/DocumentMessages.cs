using System.Globalization;
using System.Text;

namespace MoizPos.Application.Documents;

/// <summary>
/// What the customer actually reads when a bill or a receipt is sent to them.
///
/// <para><b>The figures go in the message body, not only behind the link.</b> Most customers will
/// never tap a link. An acknowledgement that works only if opened acknowledges nothing — and this
/// matters most for a payment against udhaar, which is the single most disputed event in the
/// shop.</para>
///
/// <para><b>Pure, and shared by both channels.</b> It lived on the WhatsApp builder until SMS
/// arrived and needed exactly the same words; the wording is not a property of the channel that
/// carries it. Pure so it can be tested without a database, per the constitution's preference for
/// extracting calculation from I/O.</para>
///
/// <para><b>Nothing here may be derived by the caller.</b> Every figure is passed in from what the
/// server recorded. A message assembled from a screen's numbers can disagree with the ledger, and
/// a customer holding a message that contradicts the shop's own book is worse off than one holding
/// nothing.</para>
/// </summary>
public static class DocumentMessages
{
    /// <summary>A bill: what it came to, and what is still owed on it.</summary>
    public static string Invoice(
        ShopDetails shop,
        string invoiceNumber,
        string? customerName,
        decimal total,
        decimal remaining)
    {
        var builder = new StringBuilder();

        builder.Append(CultureInfo.InvariantCulture, $"{shop.Name}, {shop.Location}");
        AppendGreeting(builder, customerName);
        builder.Append(CultureInfo.InvariantCulture, $"\nInvoice {invoiceNumber}");
        builder.Append(CultureInfo.InvariantCulture, $"\nTotal: Rs {total:N2}");

        if (remaining > 0m)
        {
            builder.Append(CultureInfo.InvariantCulture, $"\nRemaining: Rs {remaining:N2}");
        }

        builder.Append("\n\nYour receipt:");

        return builder.ToString();
    }

    /// <summary>
    /// A payment: what was received, and what is left after it.
    /// </summary>
    /// <param name="balanceAfter">
    /// The balance <b>recorded on the ledger entry for this payment</b> — never the customer's
    /// balance as it stands today. The two differ the moment the customer buys again, and a
    /// receipt that quietly restates itself contradicts the shop's own book.
    /// </param>
    public static string PaymentReceipt(
        ShopDetails shop,
        string receiptNumber,
        string? customerName,
        decimal amountReceived,
        decimal balanceAfter)
    {
        var builder = new StringBuilder();

        builder.Append(CultureInfo.InvariantCulture, $"{shop.Name}, {shop.Location}");
        AppendGreeting(builder, customerName);
        builder.Append(CultureInfo.InvariantCulture, $"\nReceipt {receiptNumber}");
        builder.Append(CultureInfo.InvariantCulture, $"\nReceived: Rs {amountReceived:N2}");

        // "Remaining: Rs 0.00" is true and reads like a fault. Nothing is owed — say that.
        builder.Append(balanceAfter > 0m
            ? string.Create(CultureInfo.InvariantCulture, $"\nRemaining: Rs {balanceAfter:N2}")
            : "\nYour account is now settled. Thank you.");

        builder.Append("\n\nYour receipt:");

        return builder.ToString();
    }

    /// <summary>
    /// Greets the customer by name where there is one. A walk-in has none, and "Hello ," reads
    /// worse than no greeting at all.
    /// </summary>
    private static void AppendGreeting(StringBuilder builder, string? customerName)
    {
        if (!string.IsNullOrWhiteSpace(customerName))
        {
            builder.Append(CultureInfo.InvariantCulture, $"\n\nHello {customerName.Trim()},");
        }
    }
}
