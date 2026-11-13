using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using ProductCatalog.Api.Contracts;

namespace ProductCatalog.Tests;

/// <summary>
/// Tests the DataAnnotations rules on <see cref="ProductRequest"/>. ASP.NET Core runs the same
/// validation automatically on every request and answers HTTP 400 when it fails.
/// </summary>
public class ProductRequestValidationTests
{
    /// <summary>Runs the same validator ASP.NET Core uses and returns the failures (empty = valid).</summary>
    private static List<ValidationResult> Validate(ProductRequest request)
    {
        var results = new List<ValidationResult>();

        // validateAllProperties: true makes [Range]/[StringLength] run too, not just [Required].
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void ValidRequest_PassesValidation()
        // Stock = 0 is valid: a product may exist while out of stock.
        => Validate(new ProductRequest { Name = "Mouse", Description = "ok", Price = 10m, Stock = 0 }).Should().BeEmpty();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankName_IsRejected(string name)
        => Validate(new ProductRequest { Name = name, Price = 10m, Stock = 1 })
            .Should().Contain(r => r.MemberNames.Contains("Name"));

    [Fact]
    public void NameLongerThan200_IsRejected()
        => Validate(new ProductRequest { Name = new string('a', 201), Price = 10m, Stock = 1 })
            .Should().Contain(r => r.MemberNames.Contains("Name"));

    [Fact]
    public void MissingPrice_IsRejected()
        // Null (field absent in the JSON) must fail instead of defaulting to 0.
        => Validate(new ProductRequest { Name = "X", Price = null, Stock = 1 })
            .Should().Contain(r => r.MemberNames.Contains("Price"));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositivePrice_IsRejected(double price)
        => Validate(new ProductRequest { Name = "X", Price = (decimal)price, Stock = 1 })
            .Should().Contain(r => r.MemberNames.Contains("Price"));

    [Fact]
    public void MissingStock_IsRejected()
        => Validate(new ProductRequest { Name = "X", Price = 1m, Stock = null })
            .Should().Contain(r => r.MemberNames.Contains("Stock"));

    [Fact]
    public void NegativeStock_IsRejected()
        => Validate(new ProductRequest { Name = "X", Price = 1m, Stock = -1 })
            .Should().Contain(r => r.MemberNames.Contains("Stock"));
}
