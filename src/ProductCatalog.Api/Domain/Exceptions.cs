namespace ProductCatalog.Api.Domain;

/// <summary>
/// Base type for expected, client-facing errors. Each carries the HTTP status code and a short title
/// that the global exception handler turns into an RFC 7807 ProblemDetails response.
/// </summary>
/// <param name="message">Human-readable explanation returned to the client as the problem "detail".</param>
/// <param name="statusCode">HTTP status code to respond with.</param>
/// <param name="title">Short, generic summary of the error type.</param>
public abstract class AppException(string message, int statusCode, string title) : Exception(message)
{
    /// <summary>HTTP status code to respond with.</summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>Short, generic summary of the error type.</summary>
    public string Title { get; } = title;
}

/// <summary>The requested resource does not exist (HTTP 404).</summary>
/// <param name="message">Explanation returned to the client.</param>
public class NotFoundException(string message) : AppException(message, StatusCodes.Status404NotFound, "Resource not found")
{
    /// <summary>Creates the exception for a missing product.</summary>
    /// <param name="id">Identifier that was looked up.</param>
    public static NotFoundException ForProduct(int id) => new($"Product with id {id} was not found.");
}

/// <summary>The request is syntactically valid but breaks a business rule, e.g. a non-positive quantity (HTTP 400).</summary>
/// <param name="message">Explanation returned to the client.</param>
public class BadRequestException(string message) : AppException(message, StatusCodes.Status400BadRequest, "Invalid request");

/// <summary>The request conflicts with the current state of the resource, e.g. insufficient stock (HTTP 409).</summary>
/// <param name="message">Explanation returned to the client.</param>
public class ConflictException(string message) : AppException(message, StatusCodes.Status409Conflict, "Conflict");
