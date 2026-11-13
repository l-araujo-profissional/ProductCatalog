using System.ComponentModel.DataAnnotations;

namespace ProductCatalog.Api.Contracts;

/// <summary>
/// Rejects decimal values that have more decimal places than allowed (e.g. 19.999 when two are allowed).
/// Trailing zeros are fine: 19.990 is the same value as 19.99.
/// It is used for prices so the API never rounds money silently: whatever the client sends is exactly
/// what is stored and returned, or the request is refused with HTTP 400.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class MaxDecimalPlacesAttribute : ValidationAttribute
{
    /// <summary>Creates the rule.</summary>
    /// <param name="maxPlaces">Maximum number of decimal places allowed (zero or greater).</param>
    public MaxDecimalPlacesAttribute(int maxPlaces)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxPlaces);
        MaxPlaces = maxPlaces;
    }

    /// <summary>Maximum number of decimal places allowed.</summary>
    public int MaxPlaces { get; }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name) => $"{name} must have at most {MaxPlaces} decimal places.";

    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // A missing value is [Required]'s job, not this rule's.
        if (value is null) return ValidationResult.Success;

        // Rounding to the allowed places changes the value only if there were digits beyond them.
        if (value is decimal d && decimal.Round(d, MaxPlaces) == d) return ValidationResult.Success;

        var members = validationContext.MemberName is null ? null : new[] { validationContext.MemberName };
        return new ValidationResult(FormatErrorMessage(validationContext.DisplayName), members);
    }
}
