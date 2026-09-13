using Dapper;
using FluentAssertions;
using MoizPos.Application.Services;
using MoizPos.Application.Time;
using MoizPos.Domain.Enums;
using MoizPos.Domain.Errors;
using MoizPos.Infrastructure.Data;
using MoizPos.Infrastructure.Repositories;
using MoizPos.IntegrationTests.Infrastructure;

namespace MoizPos.IntegrationTests.Returns;

/// <summary>
/// T112–T117 — returns. The critical distinction is between a return against an unpaid credit
/// sale (reduces what the customer owes) and one against a settled sale (creates a refund the
/// shop owes them). Silently discarding the second would quietly cheat the customer.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReturnTests
{
    private readonly ApiFactory _api;

    public ReturnTests(ApiFactory api) => _api = api;

    private MySqlConnectionFactory Factory() => new(_api.ConnectionString);

    private ReturnService Returns() =>
        new(
            new UnitOfWorkFactory(Factory()),
            new ReturnWriteRepository(),
            new InvoiceWriteRepository(),
            new PurchaseWriteRepository(),
            new StockWriteRepository(),
            new StockMovementWriter(),
            new AuditWriter(),
            new SystemClock());

    private InvoiceService Sales() =>
        new(
            new UnitOfWorkFactory(Factory()),
            new InvoiceWriteRepository(),
            new CustomerRepository(Factory()),
            new StockMovementWriter(),
            new AuditWriter(),
            new SystemClock());

    private PurchaseService Purchases() =>
        new(
            new UnitOfWorkFactory(Factory()),
            new PurchaseWriteRepository(),
            new StockMovementWriter(),
            new AuditWriter(),
            new SystemClock());

    private async Task<long> CreateProductAsync(int quantity, decimal cost, decimal salePrice)
    {
        await using var connection = await _api.OpenDatabaseAsync();

        return await connection.ExecuteScalarAsync<long>(
            """
            -- Products carry a category foreign key now, so the category has to exist first.
            INSERT IGNORE INTO categories (name, created_at_utc) VALUES ('Cables', UTC_TIMESTAMP(6));
            INSERT INTO products
                (name, category_id, cost_price, wholesale_price, retail_price, sale_price,
                 quantity_on_hand, min_stock_threshold, is_active, created_at_utc)
            VALUES (@name, (SELECT id FROM categories WHERE name = 'Cables'), @cost, 0, 0, @salePrice, @quantity, 3, TRUE, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """,
            new { name = $"Cable {Guid.NewGuid():N}"[..20], cost, salePrice, quantity });
    }

    private async Task<long> CreateCustomerAsync()
    {
        await using var connection = await _api.OpenDatabaseAsync();

        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO customers (name, outstanding_balance, is_active, created_at_utc)
            VALUES (@name, 0, TRUE, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """,
            new { name = $"Cust {Guid.NewGuid():N}"[..18] });
    }

    private async Task<long> CreateSupplierAsync()
    {
        await using var connection = await _api.OpenDatabaseAsync();

        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO suppliers (name, payable_balance, is_active, created_at_utc)
            VALUES (@name, 0, TRUE, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """,
            new { name = $"S {Guid.NewGuid():N}"[..18] });
    }

    private async Task<int> QuantityAsync(long productId)
    {
        await using var connection = await _api.OpenDatabaseAsync();

        return await connection.ExecuteScalarAsync<int>(
            "SELECT quantity_on_hand FROM products WHERE id = @productId;", new { productId });
    }

    private async Task<decimal> BalanceAsync(long customerId)
    {
        await using var connection = await _api.OpenDatabaseAsync();

        return await connection.ExecuteScalarAsync<decimal>(
            "SELECT outstanding_balance FROM customers WHERE id = @customerId;", new { customerId });
    }

    private async Task<long> FirstInvoiceItemAsync(long invoiceId)
    {
        await using var connection = await _api.OpenDatabaseAsync();

        return await connection.ExecuteScalarAsync<long>(
            "SELECT id FROM invoice_items WHERE invoice_id = @invoiceId ORDER BY id LIMIT 1;",
            new { invoiceId });
    }

    private async Task<CreateInvoiceResult> SellAsync(
        long productId, int quantity, decimal price, decimal paid, long? customerId, long userId) =>
        await Sales().CreateAsync(
            new CreateInvoiceRequest
            {
                CustomerId = customerId,
                AmountPaid = paid,
                PaymentMethod = paid == 0 ? PaymentMethod.Credit
                    : paid < price * quantity ? PaymentMethod.Partial : PaymentMethod.Cash,
                Items = [new CreateInvoiceLine { ProductId = productId, Quantity = quantity, UnitSalePrice = price }],
            }, userId, UserRole.Admin);

    // ================================================================
    //  Sale return against an UNPAID credit sale — spec US5 scenario 1
    // ================================================================

    [Fact]
    public async Task Returning_from_an_unpaid_credit_sale_reduces_the_customer_balance()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Staff);
        var customerId = await CreateCustomerAsync();
        var productId = await CreateProductAsync(10, 800m, 1100m);

        var sale = await SellAsync(productId, 2, 1100m, paid: 0m, customerId, userId);
        (await BalanceAsync(customerId)).Should().Be(2200m);

        var result = await Returns().RecordSaleReturnAsync(
            new RecordSaleReturnRequest
            {
                InvoiceId = sale.InvoiceId,
                Items = [new SaleReturnLine { InvoiceItemId = await FirstInvoiceItemAsync(sale.InvoiceId), Quantity = 1 }],
            },
            userId);

        result.TotalReturned.Should().Be(1100m);
        result.RefundDue.Should().Be(0m, "the customer had not paid, so nothing is refunded");
        result.CustomerBalance.Should().Be(1100m);

        (await QuantityAsync(productId)).Should().Be(9, "the returned unit goes back on the shelf");
        (await BalanceAsync(customerId)).Should().Be(1100m);
    }

    [Fact]
    public async Task A_return_reduces_the_invoice_net_amount()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Staff);
        var customerId = await CreateCustomerAsync();
        var productId = await CreateProductAsync(10, 800m, 1100m);

        var sale = await SellAsync(productId, 2, 1100m, 0m, customerId, userId);

        await Returns().RecordSaleReturnAsync(
            new RecordSaleReturnRequest
            {
                InvoiceId = sale.InvoiceId,
                Items = [new SaleReturnLine { InvoiceItemId = await FirstInvoiceItemAsync(sale.InvoiceId), Quantity = 1 }],
            },
            userId);

        await using var connection = await _api.OpenDatabaseAsync();

        var net = await connection.ExecuteScalarAsync<decimal>(
            "SELECT net_amount FROM invoices WHERE id = @id;", new { id = sale.InvoiceId });

        net.Should().Be(1100m);
    }

    [Fact]
    public async Task A_return_appends_a_ledger_entry()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Staff);
        var customerId = await CreateCustomerAsync();
        var productId = await CreateProductAsync(10, 800m, 1100m);

        var sale = await SellAsync(productId, 2, 1100m, 0m, customerId, userId);

        await Returns().RecordSaleReturnAsync(
            new RecordSaleReturnRequest
            {
                InvoiceId = sale.InvoiceId,
                Items = [new SaleReturnLine { InvoiceItemId = await FirstInvoiceItemAsync(sale.InvoiceId), Quantity = 1 }],
            },
            userId);

        await using var connection = await _api.OpenDatabaseAsync();

        var entry = await connection.QuerySingleAsync<(string Type, decimal Paid, decimal Balance)>(
            """
            SELECT entry_type, paid_amount, balance_after FROM ledger_entries
            WHERE customer_id = @customerId ORDER BY id DESC LIMIT 1;
            """,
            new { customerId });

        entry.Type.Should().Be("SaleReturn");
        entry.Paid.Should().Be(1100m);
        entry.Balance.Should().Be(1100m);
    }

    // ================================================================
    //  Sale return against a PAID sale — spec US5 scenario 2, FR-028
    // ================================================================

    [Fact]
    public async Task Returning_from_a_fully_paid_sale_records_a_refund_owed()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Staff);
        var productId = await CreateProductAsync(10, 800m, 1100m);

        var sale = await SellAsync(productId, 2, 1100m, paid: 2200m, customerId: null, userId);

        var result = await Returns().RecordSaleReturnAsync(
            new RecordSaleReturnRequest
            {
                InvoiceId = sale.InvoiceId,
                Items = [new SaleReturnLine { InvoiceItemId = await FirstInvoiceItemAsync(sale.InvoiceId), Quantity = 1 }],
            },
            userId);

        // The money was already handed over, so the shop owes it back — never silently discarded.
        result.RefundDue.Should().Be(1100m);
        result.CustomerBalance.Should().BeNull();
        (await QuantityAsync(productId)).Should().Be(9);
    }

    [Fact]
    public async Task A_partly_paid_sale_splits_between_balance_and_refund()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Staff);
        var customerId = await CreateCustomerAsync();
        var productId = await CreateProductAsync(10, 800m, 1000m);

        // Bill 2,000, paid 1,500 -> 500 still owed.
        var sale = await SellAsync(productId, 2, 1000m, paid: 1500m, customerId, userId);

        // Return both units: 500 cancels the debt, 1,500 comes back as a refund.
        var result = await Returns().RecordSaleReturnAsync(
            new RecordSaleReturnRequest
            {
                InvoiceId = sale.InvoiceId,
                Items = [new SaleReturnLine { InvoiceItemId = await FirstInvoiceItemAsync(sale.InvoiceId), Quantity = 2 }],
            },
            userId);

        result.TotalReturned.Should().Be(2000m);
        result.CustomerBalance.Should().Be(0m);
        result.RefundDue.Should().Be(1500m);
    }

    // ================================================================
    //  Reversal uses the RECORDED values — research.md R11
    // ================================================================

    [Fact]
    public async Task A_return_reverses_at_the_price_and_cost_recorded_on_the_sale()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Admin);
        var supplierId = await CreateSupplierAsync();
        var customerId = await CreateCustomerAsync();
        var productId = await CreateProductAsync(10, 800m, 1100m);

        var sale = await SellAsync(productId, 2, 1100m, 0m, customerId, userId);

        // A later purchase moves the product's cost to 850 and its price to 1,200.
        await Purchases().RecordPurchaseAsync(
            new RecordPurchaseRequest
            {
                SupplierId = supplierId, ProductId = productId,
                UnitCost = 850m, Quantity = 10, NewSalePrice = 1200m,
            },
            userId);

        await Returns().RecordSaleReturnAsync(
            new RecordSaleReturnRequest
            {
                InvoiceId = sale.InvoiceId,
                Items = [new SaleReturnLine { InvoiceItemId = await FirstInvoiceItemAsync(sale.InvoiceId), Quantity = 1 }],
            },
            userId);

        await using var connection = await _api.OpenDatabaseAsync();

        var returned = await connection.QuerySingleAsync<(decimal Sale, decimal Cost)>(
            "SELECT unit_sale_price, unit_cost_price FROM sale_return_items ORDER BY id DESC LIMIT 1;");

        // Reversed at 1,100 / 800 — what the sale recorded — not today's 1,200 / 850.
        returned.Sale.Should().Be(1100m);
        returned.Cost.Should().Be(800m);
    }

    // ================================================================
    //  Return limits — FR-026
    // ================================================================

    [Fact]
    public async Task Returning_more_than_was_sold_is_refused()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Staff);
        var customerId = await CreateCustomerAsync();
        var productId = await CreateProductAsync(10, 800m, 1100m);

        var sale = await SellAsync(productId, 2, 1100m, 0m, customerId, userId);

        var act = async () => await Returns().RecordSaleReturnAsync(
            new RecordSaleReturnRequest
            {
                InvoiceId = sale.InvoiceId,
                Items = [new SaleReturnLine { InvoiceItemId = await FirstInvoiceItemAsync(sale.InvoiceId), Quantity = 3 }],
            },
            userId);

        await act.Should().ThrowAsync<ReturnExceedsOriginalException>();

        (await QuantityAsync(productId)).Should().Be(8, "a refused return must not move stock");
        (await BalanceAsync(customerId)).Should().Be(2200m);
    }

    [Fact]
    public async Task A_second_return_cannot_exceed_what_remains()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Staff);
        var customerId = await CreateCustomerAsync();
        var productId = await CreateProductAsync(10, 800m, 1100m);

        var sale = await SellAsync(productId, 3, 1100m, 0m, customerId, userId);
        var itemId = await FirstInvoiceItemAsync(sale.InvoiceId);
        var returns = Returns();

        await returns.RecordSaleReturnAsync(
            new RecordSaleReturnRequest
            { InvoiceId = sale.InvoiceId, Items = [new SaleReturnLine { InvoiceItemId = itemId, Quantity = 2 }] },
            userId);

        var act = async () => await returns.RecordSaleReturnAsync(
            new RecordSaleReturnRequest
            { InvoiceId = sale.InvoiceId, Items = [new SaleReturnLine { InvoiceItemId = itemId, Quantity = 2 }] },
            userId);

        await act.Should().ThrowAsync<ReturnExceedsOriginalException>();
    }

    [Fact]
    public async Task Returning_the_remainder_is_allowed()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Staff);
        var customerId = await CreateCustomerAsync();
        var productId = await CreateProductAsync(10, 800m, 1100m);

        var sale = await SellAsync(productId, 3, 1100m, 0m, customerId, userId);
        var itemId = await FirstInvoiceItemAsync(sale.InvoiceId);
        var returns = Returns();

        await returns.RecordSaleReturnAsync(
            new RecordSaleReturnRequest
            { InvoiceId = sale.InvoiceId, Items = [new SaleReturnLine { InvoiceItemId = itemId, Quantity = 2 }] },
            userId);

        await returns.RecordSaleReturnAsync(
            new RecordSaleReturnRequest
            { InvoiceId = sale.InvoiceId, Items = [new SaleReturnLine { InvoiceItemId = itemId, Quantity = 1 }] },
            userId);

        (await BalanceAsync(customerId)).Should().Be(0m);
        (await QuantityAsync(productId)).Should().Be(10);
    }

    [Fact]
    public async Task A_return_writes_a_stock_movement()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Staff);
        var customerId = await CreateCustomerAsync();
        var productId = await CreateProductAsync(10, 800m, 1100m);

        var sale = await SellAsync(productId, 2, 1100m, 0m, customerId, userId);

        await Returns().RecordSaleReturnAsync(
            new RecordSaleReturnRequest
            {
                InvoiceId = sale.InvoiceId,
                Reason = "Faulty charger",
                Items = [new SaleReturnLine { InvoiceItemId = await FirstInvoiceItemAsync(sale.InvoiceId), Quantity = 1 }],
            },
            userId);

        await using var connection = await _api.OpenDatabaseAsync();

        var movement = await connection.QuerySingleAsync<(int Change, int Resulting, string Reason, string Note)>(
            """
            SELECT change_qty, resulting_qty, reason, note FROM stock_movements
            WHERE product_id = @productId ORDER BY id DESC LIMIT 1;
            """,
            new { productId });

        movement.Change.Should().Be(1);
        movement.Resulting.Should().Be(9);
        movement.Reason.Should().Be("SaleReturn");
        movement.Note.Should().Be("Faulty charger");
    }

    [Fact]
    public async Task A_return_gets_a_return_number()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Staff);
        var customerId = await CreateCustomerAsync();
        var productId = await CreateProductAsync(10, 800m, 1100m);

        var sale = await SellAsync(productId, 1, 1100m, 0m, customerId, userId);

        var result = await Returns().RecordSaleReturnAsync(
            new RecordSaleReturnRequest
            {
                InvoiceId = sale.InvoiceId,
                Items = [new SaleReturnLine { InvoiceItemId = await FirstInvoiceItemAsync(sale.InvoiceId), Quantity = 1 }],
            },
            userId);

        result.ReturnNumber.Should().MatchRegex(@"^SRT-\d{4}-\d{6}$");
    }

    [Fact]
    public async Task An_empty_return_is_refused()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Staff);

        var act = async () => await Returns().RecordSaleReturnAsync(
            new RecordSaleReturnRequest { InvoiceId = 1, Items = [] }, userId);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    // ================================================================
    //  Purchase returns — FR-025
    // ================================================================

    [Fact]
    public async Task A_purchase_return_reduces_stock_and_the_supplier_payable()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Admin);
        var supplierId = await CreateSupplierAsync();
        var productId = await CreateProductAsync(0, 0m, 1100m);

        var purchase = await Purchases().RecordPurchaseAsync(
            new RecordPurchaseRequest
            { SupplierId = supplierId, ProductId = productId, UnitCost = 800m, Quantity = 50 },
            userId);

        var result = await Returns().RecordPurchaseReturnAsync(
            new RecordPurchaseReturnRequest { PurchaseId = purchase.PurchaseId, Quantity = 10 },
            userId);

        // spec US5 scenario 3: 50 - 10 = 40 in stock, payable down by 10 x 800.
        result.NewQuantityOnHand.Should().Be(40);
        result.TotalReturned.Should().Be(8000m);
        result.NewSupplierPayable.Should().Be(32_000m);
    }

    [Fact]
    public async Task A_purchase_return_uses_the_original_purchase_cost()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Admin);
        var supplierId = await CreateSupplierAsync();
        var productId = await CreateProductAsync(0, 0m, 1100m);
        var purchases = Purchases();

        var first = await purchases.RecordPurchaseAsync(
            new RecordPurchaseRequest
            { SupplierId = supplierId, ProductId = productId, UnitCost = 800m, Quantity = 10 },
            userId);

        // The product's cost is now 850, but the first purchase must unwind at 800.
        await purchases.RecordPurchaseAsync(
            new RecordPurchaseRequest
            { SupplierId = supplierId, ProductId = productId, UnitCost = 850m, Quantity = 10 },
            userId);

        var result = await Returns().RecordPurchaseReturnAsync(
            new RecordPurchaseReturnRequest { PurchaseId = first.PurchaseId, Quantity = 5 },
            userId);

        result.TotalReturned.Should().Be(4000m, "5 units at the original 800, not today's 850");
    }

    [Fact]
    public async Task A_purchase_return_does_not_revert_the_products_cost()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Admin);
        var supplierId = await CreateSupplierAsync();
        var productId = await CreateProductAsync(0, 0m, 1100m);

        var purchase = await Purchases().RecordPurchaseAsync(
            new RecordPurchaseRequest
            { SupplierId = supplierId, ProductId = productId, UnitCost = 850m, Quantity = 10 },
            userId);

        await Returns().RecordPurchaseReturnAsync(
            new RecordPurchaseReturnRequest { PurchaseId = purchase.PurchaseId, Quantity = 5 },
            userId);

        await using var connection = await _api.OpenDatabaseAsync();

        var cost = await connection.ExecuteScalarAsync<decimal>(
            "SELECT cost_price FROM products WHERE id = @productId;", new { productId });

        // Documented consequence of the latest-cost rule: correcting a wrong cost needs an
        // explicit adjustment, not a return (research.md R11).
        cost.Should().Be(850m);
    }

    [Fact]
    public async Task Returning_more_than_was_purchased_is_refused()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Admin);
        var supplierId = await CreateSupplierAsync();
        var productId = await CreateProductAsync(0, 0m, 1100m);

        var purchase = await Purchases().RecordPurchaseAsync(
            new RecordPurchaseRequest
            { SupplierId = supplierId, ProductId = productId, UnitCost = 800m, Quantity = 10 },
            userId);

        var act = async () => await Returns().RecordPurchaseReturnAsync(
            new RecordPurchaseReturnRequest { PurchaseId = purchase.PurchaseId, Quantity = 11 },
            userId);

        await act.Should().ThrowAsync<ReturnExceedsOriginalException>();
        (await QuantityAsync(productId)).Should().Be(10);
    }

    [Fact]
    public async Task A_purchase_return_cannot_drive_stock_negative()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Admin);
        var supplierId = await CreateSupplierAsync();
        var productId = await CreateProductAsync(0, 0m, 1100m);

        var purchase = await Purchases().RecordPurchaseAsync(
            new RecordPurchaseRequest
            { SupplierId = supplierId, ProductId = productId, UnitCost = 800m, Quantity = 10 },
            userId);

        // Sell 8 of the 10, then try to send all 10 back to the supplier.
        await SellAsync(productId, 8, 1100m, 8800m, null, userId);

        var act = async () => await Returns().RecordPurchaseReturnAsync(
            new RecordPurchaseReturnRequest { PurchaseId = purchase.PurchaseId, Quantity = 10 },
            userId);

        await act.Should().ThrowAsync<InsufficientStockException>();
        (await QuantityAsync(productId)).Should().Be(2);
    }

    [Fact]
    public async Task A_purchase_return_is_audited()
    {
        var (userId, _, _) = await _api.CreateUserAsync(UserRole.Admin);
        var supplierId = await CreateSupplierAsync();
        var productId = await CreateProductAsync(0, 0m, 1100m);

        var purchase = await Purchases().RecordPurchaseAsync(
            new RecordPurchaseRequest
            { SupplierId = supplierId, ProductId = productId, UnitCost = 800m, Quantity = 10 },
            userId);

        await Returns().RecordPurchaseReturnAsync(
            new RecordPurchaseReturnRequest { PurchaseId = purchase.PurchaseId, Quantity = 4 },
            userId);

        await using var connection = await _api.OpenDatabaseAsync();

        var fields = (await connection.QueryAsync<string>(
            "SELECT field_name FROM audit_entries WHERE action = 'PurchaseReturn' AND user_id = @userId;",
            new { userId })).ToList();

        fields.Should().Contain("quantity_on_hand").And.Contain("payable_balance");
    }
}
