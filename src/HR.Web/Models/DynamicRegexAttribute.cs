using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace HR.Web.Models;

// The regex pattern isn't known at compile time (it's per-company, fetched from CompanySettings),
// so unlike RegularExpressionAttribute it reads the pattern from sibling properties on the same
// model instance at validation time. Passing more than one property name means "valid if it
// matches ANY of these" — used where a single field could be either a mobile or landline number.
public sealed class DynamicRegexAttribute(params string[] patternPropertyNames) : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string str || string.IsNullOrWhiteSpace(str))
            return ValidationResult.Success;

        var patterns = patternPropertyNames
            .Select(name => validationContext.ObjectType.GetProperty(name)?.GetValue(validationContext.ObjectInstance) as string)
            .Where(pattern => !string.IsNullOrEmpty(pattern))
            .ToArray();

        if (patterns.Length == 0)
            return ValidationResult.Success;

        var isValid = patterns.Any(pattern => Regex.IsMatch(str, pattern!, RegexOptions.IgnoreCase));

        return isValid
            ? ValidationResult.Success
            : new ValidationResult(ErrorMessage ?? "Invalid format.", validationContext.MemberName is null ? null : [validationContext.MemberName]);
    }
}
