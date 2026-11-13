using Microsoft.EntityFrameworkCore;
using ProductCatalog.Api.Domain;

namespace ProductCatalog.Api.Infrastructure;

/// <summary>
/// EF Core database context. The model configured here is the single source of truth (code-first):
/// EF migrations are generated from it.
/// </summary>
/// <param name="options">Provider and connection options supplied by dependency injection.</param>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>Name of the SQL Server sequence that generates product ids.</summary>
    public const string ProductIdSequence = "ProductIds";

    /// <summary>The <c>Products</c> table.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>Configures the schema: sequence, keys, column constraints, indexes and check constraints.</summary>
    /// <param name="modelBuilder">Builder used to describe the model.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // A database-owned sequence is the single source of truth for ids, so any number of API
        // instances can insert concurrently without ever producing a duplicate. It is bounded to
        // six digits; when exhausted SQL Server raises an error instead of wrapping around.
        modelBuilder.HasSequence<int>(ProductIdSequence)
            .StartsAt(100_000)
            .IncrementsBy(1)
            .HasMin(100_000)
            .HasMax(999_999);

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);

            // Use the sequence as the column default instead of an IDENTITY column.
            // EF omits Id from the INSERT and reads back the value SQL Server generated.
            entity.Property(p => p.Id)
                .HasDefaultValueSql($"NEXT VALUE FOR [{ProductIdSequence}]")
                .ValueGeneratedOnAdd();

            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Description).HasMaxLength(1000);

            // Fixed-point money: up to 18 digits, 2 of them after the decimal point.
            entity.Property(p => p.Price).HasPrecision(18, 2);

            // rowversion column used for optimistic concurrency (see Product.RowVersion).
            entity.Property(p => p.RowVersion).IsRowVersion();

            // Indexes support the name search and the stock-range queries.
            entity.HasIndex(p => p.Name);
            entity.HasIndex(p => p.Stock);

            // Last line of defence: the database itself rejects invalid values.
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Products_Stock_NonNegative", "[Stock] >= 0");
                t.HasCheckConstraint("CK_Products_Price_Positive", "[Price] > 0");
            });
        });
    }
}
