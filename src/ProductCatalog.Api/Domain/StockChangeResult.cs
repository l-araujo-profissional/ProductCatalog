namespace ProductCatalog.Api.Domain;

/// <summary>
/// Outcome of an attempt to change a product's stock. The repository returns this value instead of
/// throwing, so the service layer decides which HTTP-facing error each case maps to.
/// </summary>
public enum StockChangeResult
{
    /// <summary>The stock was updated.</summary>
    Success,

    /// <summary>No product exists with the requested id.</summary>
    NotFound,

    /// <summary>A decrement was refused because it would make the stock negative.</summary>
    InsufficientStock,

    /// <summary>An increment was refused because the result would exceed <see cref="int.MaxValue"/>.</summary>
    Overflow
}
