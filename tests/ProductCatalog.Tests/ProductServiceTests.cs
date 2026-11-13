using FluentAssertions;
using NSubstitute;
using ProductCatalog.Api.Application;
using ProductCatalog.Api.Contracts;
using ProductCatalog.Api.Domain;
using ProductCatalog.Api.Infrastructure;

namespace ProductCatalog.Tests;

/// <summary>
/// Unit tests for <see cref="ProductService"/>. The repository is replaced by a substitute, so these
/// tests verify the business rules (validation, error mapping) without any database.
/// </summary>
public class ProductServiceTests
{
    // Substitute (mock) of the data-access layer; each test configures only what it needs.
    private readonly IProductRepository _repo = Substitute.For<IProductRepository>();

    // System under test.
    private readonly ProductService _sut;

    public ProductServiceTests() => _sut = new ProductService(_repo);

    /// <summary>Builds a valid product entity with sensible defaults.</summary>
    private static Product Sample(int id = 100001, int stock = 10)
        => new() { Id = id, Name = "Sample", Price = 9.99m, Stock = stock };

    // ---------- Read ----------

    [Fact]
    public async Task GetById_WhenProductExists_ReturnsItWithAvailableStock()
    {
        // Arrange: the repository finds the product.
        _repo.GetByIdAsync(100001, Arg.Any<CancellationToken>()).Returns(Sample(stock: 7));

        // Act
        var result = await _sut.GetByIdAsync(100001);

        // Assert: the stock is exposed as AvailableStock.
        result.Id.Should().Be(100001);
        result.AvailableStock.Should().Be(7);
    }

    [Fact]
    public async Task GetById_WhenMissing_ThrowsNotFound()
    {
        // Arrange: the repository finds nothing.
        _repo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((Product?)null);

        // Act
        var act = () => _sut.GetByIdAsync(123456);

        // Assert: a missing product is reported as NotFoundException (HTTP 404).
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetAll_ReturnsAvailableStockForEveryProduct()
    {
        _repo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Product> { Sample(100001, 1), Sample(100002, 0) });

        var result = await _sut.GetAllAsync();

        // Out-of-stock products (0) must still be listed, with their stock.
        result.Select(p => p.AvailableStock).Should().Equal(1, 0);
    }

    // ---------- Create / Update / Delete ----------

    [Fact]
    public async Task Create_TrimsTextAndReturnsGeneratedId()
    {
        // Arrange: emulate the database by assigning an id when the entity is "inserted".
        _repo.AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var p = call.Arg<Product>();
                p.Id = 100500; // simulates the database sequence
                return p;
            });

        // Act: the input has surrounding whitespace.
        var result = await _sut.CreateAsync(new ProductRequest { Name = "  Mouse  ", Description = " desc ", Price = 10m, Stock = 3 });

        // Assert: text is trimmed and the generated id is returned.
        result.Id.Should().Be(100500);
        result.Name.Should().Be("Mouse");
        result.Description.Should().Be("desc");
        result.AvailableStock.Should().Be(3);
    }

    [Fact]
    public async Task Update_WhenMissing_ThrowsNotFound()
    {
        // The repository signals "no such product" by returning null.
        _repo.UpdateAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<decimal>(), Arg.Any<int>(), Arg.Any<byte[]?>(), Arg.Any<CancellationToken>())
            .Returns((Product?)null);

        var act = () => _sut.UpdateAsync(999999, new ProductRequest { Name = "X", Price = 1m, Stock = 1 });

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // A rowversion is 8 bytes; this one is arbitrary.
    private static readonly byte[] Token = [0, 0, 0, 0, 0, 0, 0x07, 0xD1];
    private static readonly string TokenText = Convert.ToBase64String(Token);

    [Fact]
    public async Task GetById_ExposesTheRowVersionAsBase64()
    {
        var product = Sample();
        product.RowVersion = Token;
        _repo.GetByIdAsync(100001, Arg.Any<CancellationToken>()).Returns(product);

        var result = await _sut.GetByIdAsync(100001);

        result.RowVersion.Should().Be(TokenText);
    }

    [Fact]
    public async Task Update_WithRowVersion_PassesTheDecodedTokenToTheRepository()
    {
        _repo.UpdateAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<decimal>(), Arg.Any<int>(), Arg.Any<byte[]?>(), Arg.Any<CancellationToken>())
            .Returns(Sample());

        await _sut.UpdateAsync(100001, new ProductRequest { Name = "X", Price = 1m, Stock = 1, RowVersion = TokenText });

        // The repository must receive the exact bytes the client sent, so the UPDATE can compare against them.
        await _repo.Received(1).UpdateAsync(100001, "X", null, 1m, 1,
            Arg.Is<byte[]?>(b => b != null && b.SequenceEqual(Token)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_WithoutRowVersion_PassesNullToTheRepository()
    {
        _repo.UpdateAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<decimal>(), Arg.Any<int>(), Arg.Any<byte[]?>(), Arg.Any<CancellationToken>())
            .Returns(Sample());

        await _sut.UpdateAsync(100001, new ProductRequest { Name = "X", Price = 1m, Stock = 1 });

        await _repo.Received(1).UpdateAsync(100001, "X", null, 1m, 1,
            Arg.Is<byte[]?>(b => b == null), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("AAAA")] // valid base64 but only 3 bytes, not a rowversion
    public async Task Update_WithMalformedRowVersion_ThrowsBadRequestAndNeverTouchesTheRepository(string token)
    {
        var act = () => _sut.UpdateAsync(100001, new ProductRequest { Name = "X", Price = 1m, Stock = 1, RowVersion = token });

        await act.Should().ThrowAsync<BadRequestException>();
        await _repo.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default, default, default, default, default);
    }

    [Fact]
    public async Task Delete_WhenMissing_ThrowsNotFound()
    {
        // The repository signals "nothing deleted" by returning false.
        _repo.DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(false);

        var act = () => _sut.DeleteAsync(999999);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Delete_WhenExists_Completes()
    {
        _repo.DeleteAsync(100001, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => _sut.DeleteAsync(100001);

        await act.Should().NotThrowAsync();
    }

    // ---------- Stock ----------

    [Fact]
    public async Task DecrementStock_Success_ReturnsUpdatedProduct()
    {
        // Arrange: the atomic update succeeds and the re-read shows the new stock.
        _repo.TryDecrementStockAsync(100001, 3, Arg.Any<CancellationToken>()).Returns(StockChangeResult.Success);
        _repo.GetByIdAsync(100001, Arg.Any<CancellationToken>()).Returns(Sample(stock: 7));

        var result = await _sut.DecrementStockAsync(100001, 3);

        result.AvailableStock.Should().Be(7);
    }

    [Fact]
    public async Task DecrementStock_InsufficientStock_ThrowsConflict()
    {
        _repo.TryDecrementStockAsync(100001, 50, Arg.Any<CancellationToken>()).Returns(StockChangeResult.InsufficientStock);

        var act = () => _sut.DecrementStockAsync(100001, 50);

        // Asking for more than available is a conflict with the current state (HTTP 409).
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task DecrementStock_UnknownProduct_ThrowsNotFound()
    {
        _repo.TryDecrementStockAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(StockChangeResult.NotFound);

        var act = () => _sut.DecrementStockAsync(999999, 1);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task StockChanges_WithNonPositiveQuantity_ThrowBadRequestAndNeverTouchTheRepository(int quantity)
    {
        var decrement = () => _sut.DecrementStockAsync(100001, quantity);
        var increment = () => _sut.AddToStockAsync(100001, quantity);

        await decrement.Should().ThrowAsync<BadRequestException>();
        await increment.Should().ThrowAsync<BadRequestException>();

        // Validation must happen before any database call.
        await _repo.DidNotReceiveWithAnyArgs().TryDecrementStockAsync(default, default, default);
        await _repo.DidNotReceiveWithAnyArgs().TryIncrementStockAsync(default, default, default);
    }

    [Fact]
    public async Task AddToStock_Success_ReturnsUpdatedProduct()
    {
        _repo.TryIncrementStockAsync(100001, 5, Arg.Any<CancellationToken>()).Returns(StockChangeResult.Success);
        _repo.GetByIdAsync(100001, Arg.Any<CancellationToken>()).Returns(Sample(stock: 15));

        var result = await _sut.AddToStockAsync(100001, 5);

        result.AvailableStock.Should().Be(15);
    }

    [Fact]
    public async Task AddToStock_Overflow_ThrowsConflict()
    {
        _repo.TryIncrementStockAsync(100001, int.MaxValue, Arg.Any<CancellationToken>()).Returns(StockChangeResult.Overflow);

        var act = () => _sut.AddToStockAsync(100001, int.MaxValue);

        await act.Should().ThrowAsync<ConflictException>();
    }

    // ---------- Search / stock level ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_WithBlankName_ThrowsBadRequest(string name)
    {
        var act = () => _sut.SearchByNameAsync(name);

        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task Search_TrimsTermAndReturnsMatches()
    {
        // The repository is only set up for the trimmed term "mouse".
        _repo.SearchByNameAsync("mouse", Arg.Any<CancellationToken>()).Returns(new List<Product> { Sample() });

        var result = await _sut.SearchByNameAsync("  mouse ");

        result.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(10, 5)]  // inverted range
    [InlineData(-1, 5)]  // negative minimum
    [InlineData(0, -1)]  // negative maximum
    public async Task StockLevel_WithInvalidRange_ThrowsBadRequest(int min, int max)
    {
        var act = () => _sut.GetByStockLevelAsync(min, max);

        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task StockLevel_WithValidRange_ReturnsProducts()
    {
        _repo.GetByStockRangeAsync(5, 20, Arg.Any<CancellationToken>())
            .Returns(new List<Product> { Sample(stock: 8), Sample(100002, 20) });

        var result = await _sut.GetByStockLevelAsync(5, 20);

        result.Should().HaveCount(2);
    }
}
