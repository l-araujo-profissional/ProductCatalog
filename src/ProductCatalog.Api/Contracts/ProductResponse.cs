using ProductCatalog.Api.Domain;

namespace ProductCatalog.Api.Contracts;

/// <summary>
/// Product representation returned by every endpoint. Using a DTO keeps the persistence entity
/// (and its concurrency token) out of the public API contract.
/// </summary>
/// <param name="Id">Six-digit product identifier.</param>
/// <param name="Name">Product name.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Price">Unit price.</param>
/// <param name="AvailableStock">Units currently available (always included, as the requirements demand).</param>
/// <param name="RowVersion">
/// Opaque concurrency token (base64). Send it back in the PUT body to make the update fail with 409 if the
/// product changed in the meantime. It changes on every update, stock changes included.
/// </param>
public record ProductResponse(int Id, string Name, string? Description, decimal Price, int AvailableStock, string? RowVersion = null)
{
    /// <summary>Maps a <see cref="Product"/> entity to its response DTO.</summary>
    /// <param name="p">Entity to map.</param>
    public static ProductResponse From(Product p) => new(
        p.Id, p.Name, p.Description, p.Price, p.Stock,
        p.RowVersion is { Length: > 0 } ? Convert.ToBase64String(p.RowVersion) : null);
}
