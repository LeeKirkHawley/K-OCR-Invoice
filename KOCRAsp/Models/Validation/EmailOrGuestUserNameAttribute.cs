using System.ComponentModel.DataAnnotations;

namespace KOCRAsp.Models.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class EmailOrGuestUserNameAttribute : ValidationAttribute
{
    private static readonly EmailAddressAttribute EmailAddressValidator = new();

    public EmailOrGuestUserNameAttribute()
    {
        ErrorMessage = "Enter a valid email address or Guest name.";
    }

    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        if (value is not string identifier)
            return false;

        identifier = identifier.Trim();
        if (identifier.Length == 0)
            return true;

        return EmailAddressValidator.IsValid(identifier) || IsGuestUserName(identifier);
    }

    private static bool IsGuestUserName(string identifier)
    {
        if (!identifier.StartsWith("Guest", StringComparison.OrdinalIgnoreCase))
            return false;

        return identifier.Length > "Guest".Length &&
               identifier["Guest".Length..].All(char.IsDigit);
    }
}
