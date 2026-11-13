using ProductCatalog.Api.Contracts;
using ProductCatalog.Api.Domain;
using ProductCatalog.Api.Infrastructure;

namespace ProductCatalog.Api.Application;

/// <summary>
/// Default <see cref="IProductService"/>. Holds the business rules (input checks, mapping of
/// repository outcomes to domain exceptions) and has no knowledge of HTTP or of EF Core,
/// which makes it easy to unit test with a mocked repository.
/// </summary>
/// <param name="repository">Data-access abstraction for products.</param>
public class ProductService(IProductRepository repository) : IProductService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductResponse>> GetAllAsync(CancellationToken ct = default)
        => (await repository.GetAllAsync(ct)).Select(ProductResponse.From).ToList();

    /// <inheritdoc />
    public async Task<ProductResponse> GetByIdAsync(int id, CancellationToken ct = default)
    {
        // A missing product becomes a 404 via NotFoundException.
        var product = await repository.GetByIdAsync(id, ct) ?? throw NotFoundException.ForProduct(id);
        return ProductResponse.From(product);
    }

    /// <inheritdoc />
    public async Task<ProductResponse> CreateAsync(ProductRequest request, CancellationToken ct = default)
    {
        // Price and Stock were validated as present, so '!.Value' is safe here.
        // Text is trimmed so that " Mouse " and "Mouse" are stored identically.
        var product = new Product
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Price = request.Price!.Value,
            Stock = request.Stock!.Value
        };

        // The Id is filled in by the database sequence during the insert.
        var created = await repository.AddAsync(product, ct);
        return ProductResponse.From(created);
    }

    /// <inheritdoc />
    public async Task<ProductResponse> UpdateAsync(int id, ProductRequest request, CancellationToken ct = default)
    {
        // The repository returns null when the product does not exist.
        var updated = await repository.UpdateAsync(
            id,
            request.Name.Trim(),
            request.Description?.Trim(),
            request.Price!.Value,
            request.Stock!.Value,
            ct) ?? throw NotFoundException.ForProduct(id);

        return ProductResponse.From(updated);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        // The repository reports whether a row was actually deleted.
        if (!await repository.DeleteAsync(id, ct))
            throw NotFoundException.ForProduct(id);
    }

    /// <inheritdoc />
    public Task<ProductResponse> DecrementStockAsync(int id, int quantity, CancellationToken ct = default)
        => ChangeStockAsync(id, quantity, increment: false, ct);

    /// <inheritdoc />
    public Task<ProductResponse> AddToStockAsync(int id, int quantity, CancellationToken ct = default)
        => ChangeStockAsync(id, quantity, increment: true, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductResponse>> SearchByNameAsync(string name, CancellationToken ct = default)
    {
        // The controller binds 'name' as required, but whitespace-only values still need rejecting.
        if (string.IsNullOrWhiteSpace(name))
            throw new BadRequestException("The 'name' query parameter is required.");

        return (await repository.SearchByNameAsync(name.Trim(), ct)).Select(ProductResponse.From).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductResponse>> GetByStockLevelAsync(int min, int max, CancellationToken ct = default)
    {
        // Stock can never be negative, so negative bounds make no sense.
        if (min < 0 || max < 0)
            throw new BadRequestException("'min' and 'max' must be zero or greater.");

        // An inverted range would always be empty; telling the caller is more helpful.
        if (min > max)
            throw new BadRequestException("'min' must be less than or equal to 'max'.");

        return (await repository.GetByStockRangeAsync(min, max, ct)).Select(ProductResponse.From).ToList();
    }

    /// <summary>
    /// Shared implementation of the two stock endpoints: validates the quantity, performs the atomic
    /// change in the repository, translates failure outcomes into exceptions, and returns the fresh product.
    /// </summary>
    /// <param name="id">Product identifier.</param>
    /// <param name="quantity">Units to add or remove.</param>
    /// <param name="increment"><c>true</c> to add stock, <c>false</c> to remove it.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    private async Task<ProductResponse> ChangeStockAsync(int id, int quantity, bool increment, CancellationToken ct)
    {
        // Zero or negative quantities are rejected before touching the database
        // (otherwise a negative "decrement" would silently add stock).
        if (quantity <= 0)
            throw new BadRequestException("Quantity must be greater than zero.");

        var result = increment
            ? await repository.TryIncrementStockAsync(id, quantity, ct)
            : await repository.TryDecrementStockAsync(id, quantity, ct);

        // Map each non-success outcome to the matching client-facing error.
        switch (result)
        {
            case StockChangeResult.NotFound:
                throw NotFoundException.ForProduct(id);
            case StockChangeResult.InsufficientStock:
                throw new ConflictException($"Insufficient stock on product {id} to remove {quantity} unit(s).");
            case StockChangeResult.Overflow:
                throw new ConflictException($"Adding {quantity} unit(s) would exceed the maximum stock supported for product {id}.");
        }

        // Success: read back the product so the response shows the new available stock.
        return await GetByIdAsync(id, ct);
    }
}
