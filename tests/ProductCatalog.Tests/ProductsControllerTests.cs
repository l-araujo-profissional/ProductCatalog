using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ProductCatalog.Api.Application;
using ProductCatalog.Api.Contracts;
using ProductCatalog.Api.Controllers;

namespace ProductCatalog.Tests;

/// <summary>
/// Unit tests for <see cref="ProductsController"/>. They check that each action returns the right
/// HTTP result type and delegates to the service; business rules are covered in <see cref="ProductServiceTests"/>.
/// </summary>
public class ProductsControllerTests
{
    // Substitute for the application service, so no business logic or database is involved.
    private readonly IProductService _service = Substitute.For<IProductService>();

    // System under test.
    private readonly ProductsController _sut;

    public ProductsControllerTests() => _sut = new ProductsController(_service);

    [Fact]
    public async Task Create_ReturnsCreatedAtActionWithTheNewProduct()
    {
        var created = new ProductResponse(100010, "Mouse", null, 10m, 5);
        _service.CreateAsync(Arg.Any<ProductRequest>(), Arg.Any<CancellationToken>()).Returns(created);

        var result = await _sut.Create(new ProductRequest { Name = "Mouse", Price = 10m, Stock = 5 }, CancellationToken.None);

        // 201 Created, pointing the Location header at GetById with the new id.
        var response = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        response.ActionName.Should().Be(nameof(ProductsController.GetById));
        response.RouteValues!["id"].Should().Be(100010);
        response.Value.Should().Be(created);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var result = await _sut.Delete(100001, CancellationToken.None);

        // 204 No Content, and the service was asked to delete exactly once.
        result.Should().BeOfType<NoContentResult>();
        await _service.Received(1).DeleteAsync(100001, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecrementStock_ReturnsOkWithUpdatedProduct()
    {
        var updated = new ProductResponse(100001, "Mouse", null, 10m, 2);
        _service.DecrementStockAsync(100001, 3, Arg.Any<CancellationToken>()).Returns(updated);

        var result = await _sut.DecrementStock(100001, 3, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(updated);
    }

    [Fact]
    public async Task AddToStock_ReturnsOkWithUpdatedProduct()
    {
        var updated = new ProductResponse(100001, "Mouse", null, 10m, 12);
        _service.AddToStockAsync(100001, 2, Arg.Any<CancellationToken>()).Returns(updated);

        var result = await _sut.AddToStock(100001, 2, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(updated);
    }

    [Fact]
    public async Task Search_DelegatesToServiceAndReturnsOk()
    {
        _service.SearchByNameAsync("mou", Arg.Any<CancellationToken>())
            .Returns(new List<ProductResponse> { new(100001, "Mouse", null, 10m, 2) });

        var result = await _sut.Search("mou", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }
}
