using HR.Web.Components.Controls;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Tests;

public class FieldAriaTests
{
    private sealed class Probe
    {
        public string? Name { get; set; }
    }

    [Fact]
    public void Build_Emits_Id_And_Required_But_Not_Invalid_For_A_Valid_Field()
    {
        var model = new Probe();
        var attributes = FieldAria.Build(new EditContext(model), model, "probe-name", nameof(Probe.Name), required: true);

        Assert.Equal("probe-name", attributes["id"]);
        Assert.Equal("true", attributes["aria-required"]);
        Assert.False(attributes.ContainsKey("aria-invalid"));
        Assert.False(attributes.ContainsKey("aria-describedby"));
    }

    [Fact]
    public void Build_Announces_Invalid_State_And_Links_The_Error_Message()
    {
        var model = new Probe();
        var context = new EditContext(model);
        var messages = new ValidationMessageStore(context);
        messages.Add(context.Field(nameof(Probe.Name)), "Name is required");
        context.NotifyValidationStateChanged();

        var attributes = FieldAria.Build(context, model, "probe-name", nameof(Probe.Name), required: true);

        Assert.Equal("true", attributes["aria-invalid"]);
        Assert.Equal("probe-name-error", attributes["aria-describedby"]);
    }

    [Fact]
    public void Build_Omits_Id_When_The_Control_Already_Has_One()
    {
        var model = new Probe();
        var attributes = FieldAria.Build(new EditContext(model), model, "probe-name", nameof(Probe.Name), required: false, emitId: false);

        Assert.False(attributes.ContainsKey("id"));
        Assert.False(attributes.ContainsKey("aria-required"));
    }

    [Fact]
    public void Build_Handles_Missing_Edit_Context_And_Property()
    {
        var attributes = FieldAria.Build(null, null, "probe-name", null, required: false);

        Assert.Single(attributes);
        Assert.Equal("probe-name", attributes["id"]);
    }
}
