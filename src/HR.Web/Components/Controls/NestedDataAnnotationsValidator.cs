using System.Collections;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Components.Controls;

// Blazor's built-in DataAnnotationsValidator only validates the properties declared directly on
// EditContext.Model — it doesn't descend into nested objects or collection items (e.g.
// CompanyDetailsEditModel.Addresses, a List<CompanyAddressEditModel>). This walks the object graph
// so attributes like [DynamicRegex]/[Required] on nested list items are enforced too.
public sealed class NestedDataAnnotationsValidator : ComponentBase, IDisposable
{
    [CascadingParameter] private EditContext CurrentEditContext { get; set; } = default!;

    private ValidationMessageStore _messageStore = default!;

    protected override void OnInitialized()
    {
        _messageStore = new ValidationMessageStore(CurrentEditContext);
        CurrentEditContext.OnValidationRequested += OnValidationRequested;
    }

    private void OnValidationRequested(object? sender, ValidationRequestedEventArgs e)
    {
        _messageStore.Clear();
        ValidateObject(CurrentEditContext.Model, new HashSet<object>(ReferenceEqualityComparer.Instance));
        CurrentEditContext.NotifyValidationStateChanged();
    }

    private void ValidateObject(object? instance, HashSet<object> visited)
    {
        if (instance is null || instance is string || !visited.Add(instance))
            return;

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);

        foreach (var result in results)
        {
            foreach (var memberName in result.MemberNames.DefaultIfEmpty(string.Empty))
                _messageStore.Add(new FieldIdentifier(instance, memberName), result.ErrorMessage ?? "Invalid value.");
        }

        foreach (var prop in instance.GetType().GetProperties())
        {
            if (prop.GetIndexParameters().Length > 0)
                continue;

            var value = prop.GetValue(instance);
            switch (value)
            {
                case null or string:
                    continue;
                case IEnumerable enumerable:
                    foreach (var item in enumerable)
                        ValidateObject(item, visited);
                    break;
                default:
                    if (prop.PropertyType.IsClass)
                        ValidateObject(value, visited);
                    break;
            }
        }
    }

    public void Dispose() => CurrentEditContext.OnValidationRequested -= OnValidationRequested;
}
