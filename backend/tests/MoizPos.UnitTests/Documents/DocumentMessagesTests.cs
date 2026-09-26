using FluentAssertions;
using MoizPos.Application.Documents;

namespace MoizPos.UnitTests.Documents;

/// <summary>
/// The words a customer actually reads.
///
/// <para>Pure, so the wording can be argued about without a database in the way — and so the one
/// case that reads badly, a settled account reported as "Remaining: Rs 0.00", is caught here
/// rather than by a customer.</para>
/// </summary>
public sealed class DocumentMessagesTests
{
    private static readonly ShopDetails Shop = new()
    {
        Name = "Moiz Mobile & Corporation",
        Location = "Danwran Lodhran",
    };

    [Fact]
    public void A_payment_states_what_was_received_and_what_is_left()
    {
        var message = DocumentMessages.PaymentReceipt(
            Shop, "RCP-2026-000012", "Akhlaq", amountReceived: 500m, balanceAfter: 500m);

        message.Should().Contain("Hello Akhlaq,");
        message.Should().Contain("Received: Rs 500.00");
        message.Should().Contain("Remaining: Rs 500.00");
    }

    [Fact]
    public void A_settled_account_is_said_in_words_not_as_a_zero()
    {
        var message = DocumentMessages.PaymentReceipt(
            Shop, "RCP-1", "Akhlaq", amountReceived: 1000m, balanceAfter: 0m);

        // "Remaining: Rs 0.00" is true and reads like a fault.
        message.Should().NotContain("Rs 0.00");
        message.Should().Contain("settled");
    }

    [Fact]
    public void A_customer_with_no_name_is_not_greeted_as_nobody()
    {
        var message = DocumentMessages.PaymentReceipt(
            Shop, "RCP-1", null, amountReceived: 100m, balanceAfter: 0m);

        // "Hello ," reads worse than no greeting at all.
        message.Should().NotContain("Hello");
        message.Should().Contain("Received: Rs 100.00");
    }

    [Fact]
    public void An_invoice_states_the_total_and_what_is_still_owed()
    {
        var message = DocumentMessages.Invoice(
            Shop, "INV-2026-000077", "Bilal", total: 2000m, remaining: 1500m);

        message.Should().Contain("Hello Bilal,");
        message.Should().Contain("Total: Rs 2,000.00");
        message.Should().Contain("Remaining: Rs 1,500.00");
    }

    [Fact]
    public void A_fully_paid_invoice_says_nothing_about_a_balance()
    {
        var message = DocumentMessages.Invoice(
            Shop, "INV-1", "Bilal", total: 2000m, remaining: 0m);

        // Nothing is owed, so there is nothing to report. A "Remaining: Rs 0.00" line invites
        // the customer to wonder whether it should have been something else.
        message.Should().NotContain("Remaining");
    }

    [Fact]
    public void Every_message_names_the_shop_the_customer_bought_from()
    {
        DocumentMessages.Invoice(Shop, "INV-1", "Bilal", 100m, 0m)
            .Should().StartWith("Moiz Mobile & Corporation, Danwran Lodhran");

        DocumentMessages.PaymentReceipt(Shop, "RCP-1", "Bilal", 100m, 0m)
            .Should().StartWith("Moiz Mobile & Corporation, Danwran Lodhran");
    }

    [Fact]
    public void A_message_ends_by_introducing_the_link_that_follows_it()
    {
        // The link is appended by the channel builder; the message has to lead into it or it
        // arrives as a bare URL with no explanation.
        DocumentMessages.PaymentReceipt(Shop, "RCP-1", "Bilal", 100m, 50m)
            .Should().EndWith("Your receipt:");
    }
}
