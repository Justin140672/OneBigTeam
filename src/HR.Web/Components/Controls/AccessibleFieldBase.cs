using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Components.Controls;

public abstract class AccessibleFieldBase<TValue> : ComponentBase, IDisposable
{
    [Parameter, EditorRequired] public string Label { get; set; } = string.Empty;

    [Parameter] public string? HelpText { get; set; }

    [Parameter] public bool Required { get; set; }

    [Parameter] public string? Id { get; set; }

    [Parameter] public TValue? Value { get; set; }
    [Parameter] public EventCallback<TValue?> ValueChanged { get; set; }
    [Parameter] public Expression<Func<TValue?>>? ValueExpression { get; set; }

    [Parameter] public Dictionary<string, object>? HtmlAttributes { get; set; }

    [Parameter] public string LabelClass { get; set; } = "form-label";

    [Parameter] public string? FieldClass { get; set; }

    [Parameter] public string? ValidationClass { get; set; }

    protected string ValidationMessageClass => string.IsNullOrWhiteSpace(ValidationClass) ? "validation-message" : ValidationClass;

    protected string WrapperClass => string.IsNullOrWhiteSpace(FieldClass) ? "hr-field" : $"hr-field {FieldClass}";

    [CascadingParameter] protected EditContext? EditContext { get; set; }

    private readonly string _generatedId = $"hrf-{Guid.NewGuid():N}";

    protected string FieldId => string.IsNullOrWhiteSpace(Id) ? _generatedId : Id!;
    protected string LabelId => $"{FieldId}-label";
    protected string HelpId => $"{FieldId}-help";
    protected string ErrorId => $"{FieldId}-error";

    protected bool HasHelp => !string.IsNullOrWhiteSpace(HelpText);

    protected FieldIdentifier? Field =>
        ValueExpression is null ? null : FieldIdentifier.Create(ValueExpression);

    protected bool IsInvalid =>
        EditContext is not null && Field is { } f && EditContext.GetValidationMessages(f).Any();

    protected string? DescribedBy
    {
        get
        {
            var parts = new List<string>(2);
            if (HasHelp) parts.Add(HelpId);
            if (IsInvalid) parts.Add(ErrorId);
            return parts.Count == 0 ? null : string.Join(' ', parts);
        }
    }

    protected Dictionary<string, object> BuildInputAttributes(IEnumerable<KeyValuePair<string, object>>? extra = null)
    {
        var attrs = new Dictionary<string, object>
        {
            ["aria-label"] = Label,
            ["aria-labelledby"] = LabelId,
        };

        if (DescribedBy is { } db) attrs["aria-describedby"] = db;
        if (Required) attrs["aria-required"] = "true";
        if (IsInvalid) attrs["aria-invalid"] = "true";

        if (extra is not null)
            foreach (var kv in extra) attrs[kv.Key] = kv.Value;

        if (HtmlAttributes is not null)
            foreach (var kv in HtmlAttributes) attrs[kv.Key] = kv.Value;

        return attrs;
    }

    protected Task NotifyValueChanged(TValue? value)
    {
        Value = value;
        return ValueChanged.InvokeAsync(value);
    }

    private EditContext? _subscribedEditContext;

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_subscribedEditContext, EditContext))
            return;

        if (_subscribedEditContext is not null)
            _subscribedEditContext.OnValidationStateChanged -= OnValidationStateChanged;

        _subscribedEditContext = EditContext;

        if (_subscribedEditContext is not null)
            _subscribedEditContext.OnValidationStateChanged += OnValidationStateChanged;
    }

    private void OnValidationStateChanged(object? sender, ValidationStateChangedEventArgs e) =>
        _ = InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        if (_subscribedEditContext is not null)
            _subscribedEditContext.OnValidationStateChanged -= OnValidationStateChanged;
    }
}
