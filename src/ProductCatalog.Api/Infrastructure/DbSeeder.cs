using Microsoft.EntityFrameworkCore;
using ProductCatalog.Api.Domain;

namespace ProductCatalog.Api.Infrastructure;

/// <summary>
/// Seeds sample data at startup. Ids are deliberately NOT hard-coded (as <c>HasData</c> would require):
/// they come from the same sequence as every other product, so seeded rows can never collide
/// with products created later.
/// </summary>
public static class DbSeeder
{
    /// <summary>Inserts the sample products if, and only if, the table is empty (idempotent).</summary>
    /// <param name="db">Database context to seed.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        // Never touch a database that already has data.
        if (await db.Products.AnyAsync(ct)) return;

        // A variety of stock levels (including 0) makes the stock-level endpoint easy to try out.
        db.Products.AddRange(
            new Product { Name = "Wireless Mouse", Description = "2.4 GHz ergonomic mouse", Price = 19.99m, Stock = 120 },
            new Product { Name = "Mechanical Keyboard", Description = "Tenkeyless, brown switches", Price = 79.90m, Stock = 45 },
            new Product { Name = "USB-C Hub", Description = "7-in-1 hub with HDMI", Price = 34.50m, Stock = 0 },
            new Product { Name = "27\" Monitor", Description = "QHD IPS display", Price = 289.00m, Stock = 12 },
            new Product { Name = "Laptop Stand", Description = "Adjustable aluminium stand", Price = 29.00m, Stock = 60 },
            new Product { Name = "Noise-Cancelling Headphones", Description = "Bluetooth over-ear", Price = 149.99m, Stock = 8 },
            new Product { Name = "Webcam HD", Description = "1080p with privacy shutter", Price = 49.90m, Stock = 33 },
            new Product { Name = "Wireless Charger", Description = "15 W Qi charging pad", Price = 24.90m, Stock = 5 },
            new Product { Name = "External SSD 1TB", Description = "USB 3.2 portable drive", Price = 99.00m, Stock = 27 },
            new Product { Name = "Desk Lamp", Description = "LED lamp with dimmer", Price = 22.00m, Stock = 74 });

        await db.SaveChangesAsync(ct);
    }
}
