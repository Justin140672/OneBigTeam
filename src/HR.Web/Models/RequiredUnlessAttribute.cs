using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

// Employee Number is required except when the company's numbering mode is Automatic and this is
// a brand-new employee (in which case the field isn't shown at all and the value is left empty
// for the server to auto-generate) — the flag is read from a sibling bool property on the same
// model instance at validation time, same pattern as DynamicRegexAttribute above.
public sealed class RequiredUnlessAttribute(string flagPropertyName) : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var flag = validationContext.ObjectType.GetProperty(flagPropertyName)?.GetValue(validationContext.ObjectInstance) as bool?;
        if (flag == true)
            return ValidationResult.Success;

        var isEmpty = value is null || (value is string str && string.IsNullOrWhiteSpace(str));
        return isEmpty
            ? new ValidationResult(ErrorMessage ?? "This field is required.", validationContext.MemberName is null ? null : [validationContext.MemberName])
            : ValidationResult.Success;
    }
}
