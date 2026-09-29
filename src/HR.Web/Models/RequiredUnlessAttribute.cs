using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

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
