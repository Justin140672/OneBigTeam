using System.Collections;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Components.Controls;

public sealed class NestedDataAnnotationsValidator : ComponentBase, IDisposable
{
    [CascadingParameter] private EditContext CurrentEditContext { get; set; } = default!;

    private ValidationMessageStore _messageStore = default!;

    protected override void OnInitialized()
    {
        _messageStore = new ValidationMessageStore(CurrentEditContext);
        CurrentEditContext.OnValidationRequested += OnValidationRequested;

        CurrentEditContext.OnFieldChanged += OnFieldChanged;
    }

    private void OnValidationRequested(object? sender, ValidationRequestedEventArgs e)
    {
        _messageStore.Clear();
        foreach (var (field, message) in ValidateNestedObjects(CurrentEditContext.Model))
            _messageStore.Add(field, message);
        CurrentEditContext.NotifyValidationStateChanged();
    }

    // The stock DataAnnotationsValidator already validates the changed field on any model instance,
    // so re-adding it here would show the same message twice; only drop this store's stale entry.
    private void OnFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        _messageStore.Clear(e.FieldIdentifier);
        CurrentEditContext.NotifyValidationStateChanged();
    }

    // Validates every object reachable from root, excluding root itself (the stock validator owns it).
    internal static List<(FieldIdentifier Field, string Message)> ValidateNestedObjects(object root)
    {
        var errors = new List<(FieldIdentifier, string)>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { root };
        ValidateChildren(root, visited, errors);
        return errors;
    }

    private static void ValidateObject(object? instance, HashSet<object> visited, List<(FieldIdentifier, string)> errors)
    {
        if (instance is null || instance is string || !visited.Add(instance))
            return;

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);

        foreach (var result in results)
        {
            foreach (var memberName in result.MemberNames.DefaultIfEmpty(string.Empty))
                errors.Add((new FieldIdentifier(instance, memberName), result.ErrorMessage ?? "Invalid value."));
        }

        ValidateChildren(instance, visited, errors);
    }

    private static void ValidateChildren(object instance, HashSet<object> visited, List<(FieldIdentifier, string)> errors)
    {
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
                        ValidateObject(item, visited, errors);
                    break;
                default:
                    if (prop.PropertyType.IsClass)
                        ValidateObject(value, visited, errors);
                    break;
            }
        }
    }

    public void Dispose()
    {
        CurrentEditContext.OnValidationRequested -= OnValidationRequested;
        CurrentEditContext.OnFieldChanged -= OnFieldChanged;
    }
}
