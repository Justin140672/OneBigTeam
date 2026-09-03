using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Components.Controls;

/// <summary>
/// Shared plumbing for the accessible form-field wrappers (Hr*Field). Every wrapper renders a real
/// visible &lt;label&gt; that is programmatically associated with the Syncfusion input, and pushes an
/// accessible name / description / required / invalid state onto the rendered control via the
/// control's <c>HtmlAttributes</c> parameter (the attribute bag Syncfusion Blazor inputs splat onto
/// their underlying &lt;input&gt; element — verified against SfTextBox/SfNumericTextBox/
/// SfDropDownList/SfComboBox/SfDatePicker/SfCheckBox).
///
/// Screen readers previously announced these fields only as "textbox"/"numerictextbox"/
/// "dropdownlist" with no name because the sighted-only &lt;label class="form-label"&gt; markup was
/// never tied to the control (no for/id, no aria-*).
/// </summary>
public abstract class AccessibleFieldBase<TValue> : ComponentBase
{
    /// <summary>Visible label text. Also becomes the input's accessible name.</summary>
    [Parameter, EditorRequired] public string Label { get; set; } = string.Empty;

    /// <summary>Optional hint text rendered under the field and linked via aria-describedby.</summary>
    [Parameter] public string? HelpText { get; set; }

    /// <summary>Marks the field required: renders a "*" in the label and sets aria-required.</summary>
    [Parameter] public bool Required { get; set; }

    /// <summary>
    /// Explicit id for the input. When omitted a stable per-instance id is generated so the
    /// label's for/id association always works.
    /// </summary>
    [Parameter] public string? Id { get; set; }

    [Parameter] public TValue? Value { get; set; }
    [Parameter] public EventCallback<TValue?> ValueChanged { get; set; }
    [Parameter] public Expression<Func<TValue?>>? ValueExpression { get; set; }

    /// <summary>Extra attributes forwarded onto the underlying Syncfusion input.</summary>
    [Parameter] public Dictionary<string, object>? HtmlAttributes { get; set; }

    /// <summary>CSS class for the visible &lt;label&gt;. Defaults to Bootstrap's "form-label".</summary>
    [Parameter] public string LabelClass { get; set; } = "form-label";

    /// <summary>Extra CSS class appended to the field's outer wrapper (which always has "hr-field").</summary>
    [Parameter] public string? FieldClass { get; set; }

    /// <summary>CSS class applied to the rendered &lt;ValidationMessage&gt;.</summary>
    [Parameter] public string? ValidationClass { get; set; }

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

    /// <summary>
    /// Builds the attribute bag handed to the Syncfusion control: the generated id (so the
    /// &lt;label for&gt; resolves), an explicit aria-label fallback plus aria-labelledby pointing at
    /// the visible label, aria-describedby for help/validation text, and required/invalid state.
    /// Any caller-supplied <see cref="HtmlAttributes"/> win over the defaults.
    /// </summary>
    protected Dictionary<string, object> BuildInputAttributes(IEnumerable<KeyValuePair<string, object>>? extra = null)
    {
        var attrs = new Dictionary<string, object>
        {
            ["id"] = FieldId,
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
}
