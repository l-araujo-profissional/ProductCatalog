using Microsoft.EntityFrameworkCore;
using ProductCatalog.Api.Domain;

namespace ProductCatalog.Api.Infrastructure;

/// <summary>EF Core implementation of <see cref="IProductRepository"/> backed by SQL Server.</summary>
/// <param name="db">Database context (scoped per HTTP request).</param>
public class ProductRepository(AppDbContext db) : IProductRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default)
        // AsNoTracking: read-only query, so EF skips change tracking (faster, less memory).
        => await db.Products.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct);

    /// <inheritdoc />
    public Task<Product?> GetByIdAsync(int id, CancellationToken ct = default)
        => db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <inheritdoc />
    public async Task<Product> AddAsync(Product product, CancellationToken ct = default)
    {
        db.Products.Add(product);
        await db.SaveChangesAsync(ct); // Id is filled in from the database sequence.
        return product;
    }

    /// <inheritdoc />
    public async Task<Product?> UpdateAsync(int id, string name, string? description, decimal price, int stock, CancellationToken ct = default)
    {
        // Load the tracked entity so EF generates an UPDATE that includes the RowVersion check.
        var entity = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return null;

        entity.Name = name;
        entity.Description = description;
        entity.Price = price;
        entity.Stock = stock;

        // A concurrent modification raises DbUpdateConcurrencyException (RowVersion) -> HTTP 409.
        await db.SaveChangesAsync(ct);
        return entity;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        // Single DELETE statement; the affected-row count tells us whether the product existed.
        => await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync(ct) > 0;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Product>> SearchByNameAsync(string name, CancellationToken ct = default)
        => await db.Products.AsNoTracking()
            .Where(p => p.Name.Contains(name)) // translated to a LIKE; SQL Server's default collation is case-insensitive
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Product>> GetByStockRangeAsync(int min, int max, CancellationToken ct = default)
        => await db.Products.AsNoTracking()
            .Where(p => p.Stock >= min && p.Stock <= max)
            .OrderBy(p => p.Stock)
            .ThenBy(p => p.Id) // tie-breaker keeps the order deterministic
            .ToListAsync(ct);

    // Stock changes are single atomic UPDATE statements with the business rule in the WHERE clause.
    // This avoids the read-modify-write race that would lose updates when several instances
    // (or several requests) touch the same product at the same time.

    /// <inheritdoc />
    public async Task<StockChangeResult> TryDecrementStockAsync(int id, int quantity, CancellationToken ct = default)
    {
        // The "Stock >= quantity" condition guarantees stock never goes negative, even under concurrency.
        var affected = await db.Products
            .Where(p => p.Id == id && p.Stock >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - quantity), ct);

        if (affected > 0) return StockChangeResult.Success;

        // No row updated: either the product is missing or it has too little stock. Tell them apart.
        return await db.Products.AnyAsync(p => p.Id == id, ct)
            ? StockChangeResult.InsufficientStock
            : StockChangeResult.NotFound;
    }

    /// <inheritdoc />
    public async Task<StockChangeResult> TryIncrementStockAsync(int id, int quantity, CancellationToken ct = default)
    {
        // "Stock <= int.MaxValue - quantity" is the overflow-safe form of "Stock + quantity <= int.MaxValue".
        var limit = int.MaxValue - quantity;

        var affected = await db.Products
            .Where(p => p.Id == id && p.Stock <= limit)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + quantity), ct);

        if (affected > 0) return StockChangeResult.Success;

        // No row updated: either the product is missing or the addition would overflow.
        return await db.Products.AnyAsync(p => p.Id == id, ct)
            ? StockChangeResult.Overflow
            : StockChangeResult.NotFound;
    }
}
