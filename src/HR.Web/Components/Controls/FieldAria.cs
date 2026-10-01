using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Components.Controls;

public static class FieldAria
{
    public static Dictionary<string, object> Build(
        EditContext? editContext, object? model, string id, string? property, bool required, bool emitId = true)
    {
        var attributes = new Dictionary<string, object>();

        if (emitId)
            attributes["id"] = id;

        if (required)
            attributes["aria-required"] = "true";

        if (editContext is not null && model is not null && !string.IsNullOrEmpty(property)
            && editContext.GetValidationMessages(new FieldIdentifier(model, property)).Any())
        {
            attributes["aria-invalid"] = "true";
            attributes["aria-describedby"] = $"{id}-error";
        }

        return attributes;
    }
}
