using System.Reflection;
using FluentAssertions;
using FluentValidation.TestHelper;
using MoizPos.Application.Contracts.Products;

namespace MoizPos.UnitTests.Products;

/// <summary>T047 — product validation rules, written before the validator exists.</summary>
public sealed class ProductValidatorTests
{
    private readonly ProductUpsertValidator _validator = new();

    private static ProductUpsertRequest Valid() => new()
    {
        Name = "Type-C Braided 2m",
        CategoryId = 1,
        BrandId = 2,
        Model = "CATZ-01",
        Barcode = "8901234567890",
        CostPrice = 800m,
        WholesalePrice = 950m,
        RetailPrice = 1200m,
        SalePrice = 1100m,
        QuantityOnHand = 10,
        MinStockThreshold = 3,
    };

    [Fact]
    public void Accepts_a_valid_product()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Requires_a_name(string name)
    {
        _validator.TestValidate(Valid() with { Name = name })
            .ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Rejects_a_name_over_150_characters()
    {
        _validator.TestValidate(Valid() with { Name = new string('x', 151) })
            .ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Requires_a_category_to_be_chosen()
    {
        // 0 is what an unset dropdown posts. Free-text categories are gone: a product is filed
        // against a row in the Categories module or it is not accepted.
        _validator.TestValidate(Valid() with { CategoryId = 0 })
            .ShouldHaveValidationErrorFor(x => x.CategoryId);
    }

    [Fact]
    public void Rejects_a_brand_id_that_is_not_a_real_id()
    {
        _validator.TestValidate(Valid() with { BrandId = 0 })
            .ShouldHaveValidationErrorFor(x => x.BrandId);
    }

    [Fact]
    public void Rejects_a_barcode_over_64_characters()
    {
        _validator.TestValidate(Valid() with { Barcode = new string('9', 65) })
            .ShouldHaveValidationErrorFor(x => x.Barcode);
    }

    [Fact]
    public void Allows_an_absent_barcode_brand_and_model()
    {
        // Unbranded generic stock is normal in this trade.
        var request = Valid() with { Barcode = null, BrandId = null, Model = null };

        _validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Rejects_a_negative_cost_price()
    {
        _validator.TestValidate(Valid() with { CostPrice = -1m })
            .ShouldHaveValidationErrorFor(x => x.CostPrice);
    }

    [Fact]
    public void Rejects_a_negative_sale_price()
    {
        _validator.TestValidate(Valid() with { SalePrice = -0.01m })
            .ShouldHaveValidationErrorFor(x => x.SalePrice);
    }

    [Fact]
    public void Rejects_a_negative_quantity()
    {
        _validator.TestValidate(Valid() with { QuantityOnHand = -1 })
            .ShouldHaveValidationErrorFor(x => x.QuantityOnHand);
    }

    [Fact]
    public void Rejects_a_negative_threshold()
    {
        _validator.TestValidate(Valid() with { MinStockThreshold = -1 })
            .ShouldHaveValidationErrorFor(x => x.MinStockThreshold);
    }

    [Fact]
    public void Allows_zero_prices_and_quantities()
    {
        var request = Valid() with
        {
            CostPrice = 0m,
            WholesalePrice = 0m,
            RetailPrice = 0m,
            SalePrice = 0m,
            QuantityOnHand = 0,
            MinStockThreshold = 0,
        };

        _validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Allows_a_sale_price_below_cost_because_the_shopkeeper_may_choose_to()
    {
        // Clearing old stock at a loss is a real decision, not a data error.
        _validator.TestValidate(Valid() with { CostPrice = 800m, SalePrice = 700m })
            .ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class AdjustStockValidatorTests
{
    private readonly AdjustStockValidator _validator = new();

    [Fact]
    public void Requires_a_note_explaining_the_adjustment()
    {
        _validator.TestValidate(new AdjustStockRequest { NewQuantity = 5, Note = "" })
            .ShouldHaveValidationErrorFor(x => x.Note);
    }

    [Fact]
    public void Rejects_a_negative_quantity()
    {
        _validator.TestValidate(new AdjustStockRequest { NewQuantity = -1, Note = "Recount" })
            .ShouldHaveValidationErrorFor(x => x.NewQuantity);
    }

    [Fact]
    public void Accepts_a_recount_to_zero_with_a_note()
    {
        _validator.TestValidate(new AdjustStockRequest { NewQuantity = 0, Note = "Damaged in transit" })
            .ShouldNotHaveAnyValidationErrors();
    }
}

/// <summary>T054 — cost confidentiality is a property of the type, not of a runtime check.</summary>
public sealed class ProductDtoTests
{
    private static readonly string[] ForbiddenFragments = ["cost", "profit", "margin", "wholesale"];

    [Fact]
    public void Staff_dto_exposes_no_cost_property()
    {
        var offenders = typeof(ProductStaffDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => ForbiddenFragments.Any(f =>
                name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        offenders.Should().BeEmpty(
            "FR-040: Staff must not receive cost data by any route, so the property must not exist");
    }

    [Fact]
    public void Admin_dto_does_expose_the_cost_price()
    {
        typeof(ProductAdminDto)
            .GetProperty(nameof(ProductAdminDto.CostPrice))
            .Should().NotBeNull();
    }

    [Fact]
    public void Admin_dto_inherits_everything_staff_can_see()
    {
        typeof(ProductAdminDto).Should().BeDerivedFrom<ProductStaffDto>();
    }
}
