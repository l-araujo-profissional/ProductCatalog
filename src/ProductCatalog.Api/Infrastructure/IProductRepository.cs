using ProductCatalog.Api.Domain;

namespace ProductCatalog.Api.Infrastructure;

/// <summary>
/// Data-access abstraction for <see cref="Product"/>. Keeping it behind an interface lets the
/// business logic be unit tested without a database.
/// </summary>
public interface IProductRepository
{
    /// <summary>Returns all products ordered by id (read-only, untracked).</summary>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns the product with the given id, or <c>null</c> if it does not exist.</summary>
    /// <param name="id">Product identifier.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<Product?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Inserts a product; the returned entity has its database-generated id populated.</summary>
    /// <param name="product">Product to insert (its Id must be unset).</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<Product> AddAsync(Product product, CancellationToken ct = default);

    /// <summary>Replaces the editable fields of a product; returns <c>null</c> if it does not exist.</summary>
    /// <param name="id">Product identifier.</param>
    /// <param name="name">New name.</param>
    /// <param name="description">New description.</param>
    /// <param name="price">New price.</param>
    /// <param name="stock">New stock.</param>
    /// <param name="expectedRowVersion">
    /// Concurrency token the caller last saw. When not <c>null</c> the update only succeeds if the row still has
    /// this version (otherwise <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>);
    /// when <c>null</c> only the short window between loading and saving the row is protected.
    /// </param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<Product?> UpdateAsync(int id, string name, string? description, decimal price, int stock, byte[]? expectedRowVersion, CancellationToken ct = default);

    /// <summary>Deletes a product; returns <c>false</c> if it did not exist.</summary>
    /// <param name="id">Product identifier.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Returns products whose name contains <paramref name="name"/> (partial, case-insensitive).</summary>
    /// <param name="name">Text to look for.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<IReadOnlyList<Product>> SearchByNameAsync(string name, CancellationToken ct = default);

    /// <summary>Returns products whose stock is within the inclusive range [<paramref name="min"/>, <paramref name="max"/>].</summary>
    /// <param name="min">Lowest stock to include.</param>
    /// <param name="max">Highest stock to include.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<IReadOnlyList<Product>> GetByStockRangeAsync(int min, int max, CancellationToken ct = default);

    /// <summary>Atomically removes stock if enough is available.</summary>
    /// <param name="id">Product identifier.</param>
    /// <param name="quantity">Units to remove (greater than zero).</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns><see cref="StockChangeResult.Success"/>, <see cref="StockChangeResult.NotFound"/> or <see cref="StockChangeResult.InsufficientStock"/>.</returns>
    Task<StockChangeResult> TryDecrementStockAsync(int id, int quantity, CancellationToken ct = default);

    /// <summary>Atomically adds stock if the result does not overflow.</summary>
    /// <param name="id">Product identifier.</param>
    /// <param name="quantity">Units to add (greater than zero).</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns><see cref="StockChangeResult.Success"/>, <see cref="StockChangeResult.NotFound"/> or <see cref="StockChangeResult.Overflow"/>.</returns>
    Task<StockChangeResult> TryIncrementStockAsync(int id, int quantity, CancellationToken ct = default);
}
