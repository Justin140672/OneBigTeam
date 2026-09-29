using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace HR.Web.Models;

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
