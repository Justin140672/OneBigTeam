using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

public static class A11yAssert
{
    private static readonly Regex GenericControlName = new(
        "^\\s*-\\s*(combobox|textbox|spinbutton|searchbox)\\s+\"(dropdownlist|datepicker|numerictextbox|textbox|combobox|autocomplete|multiselect)\"",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public static async Task NoGenericControlNamesAsync(IPage page, string context)
    {
        var snapshot = await page.Locator("body").AriaSnapshotAsync();
        var match = GenericControlName.Match(snapshot);
        Assert.False(match.Success,
            $"{context}: found a control exposing a generic widget name as its accessible name: {match.Value.Trim()}");
    }

    public static Task NamedAsync(ILocator scope, AriaRole role, string name, bool exact = true) =>
        Assertions.Expect(scope.GetByRole(role, new() { Name = name, Exact = exact }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

    public static async Task InputAttributesAsync(ILocator input, string type, string? autocomplete)
    {
        await Assertions.Expect(input).ToHaveAttributeAsync("type", type);
        if (autocomplete is not null)
            await Assertions.Expect(input).ToHaveAttributeAsync("autocomplete", autocomplete);
        await Assertions.Expect(input).Not.ToHaveAttributeAsync("autocomplete", "on");
    }

    public static async Task NoAutocompleteOnAsync(ILocator scope)
    {
        var count = await scope.Locator("[autocomplete='on']").CountAsync();
        Assert.Equal(0, count);
    }

    public static async Task LabelledAsync(IPage page, string fieldId, string labelText)
    {
        var field = page.Locator($"#{fieldId}");
        await Assertions.Expect(field).ToHaveCountAsync(1, new() { Timeout = 15_000 });
        await Assertions.Expect(page.Locator($"label[for='{fieldId}']")).ToContainTextAsync(labelText);
        await Assertions.Expect(field).ToHaveAttributeAsync("aria-label", new Regex($"^{Regex.Escape(labelText)}$"));
    }

    public static Task RequiredAsync(ILocator field) =>
        Assertions.Expect(field).ToHaveAttributeAsync("aria-required", "true");

    public static async Task InvalidWithAssociatedMessageAsync(IPage page, ILocator field)
    {
        await Assertions.Expect(field).ToHaveAttributeAsync("aria-invalid", "true", new() { Timeout = 15_000 });

        var describedBy = await field.GetAttributeAsync("aria-describedby");
        Assert.False(string.IsNullOrWhiteSpace(describedBy), "Invalid field has no aria-describedby.");

        var errorId = describedBy!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(t => t.EndsWith("-error", StringComparison.Ordinal));
        Assert.False(errorId is null, $"aria-describedby '{describedBy}' does not reference an error element.");

        var error = page.Locator($"#{errorId}");
        await Assertions.Expect(error).ToHaveCountAsync(1);
        await Assertions.Expect(error).ToHaveAttributeAsync("role", "alert");
        await Assertions.Expect(error).Not.ToBeEmptyAsync(new() { Timeout = 15_000 });
    }

    public static async Task IsDisabledOrReadOnlyAsync(ILocator control)
    {
        await control.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        var result = await control.EvaluateAsync<bool>(
            @"el => {
                const wrapper = el.closest('.e-control-wrapper, .e-input-group, .e-checkbox-wrapper');
                return el.disabled === true
                    || el.readOnly === true
                    || el.getAttribute('aria-disabled') === 'true'
                    || el.getAttribute('aria-readonly') === 'true'
                    || (wrapper !== null && (wrapper.classList.contains('e-disabled') || wrapper.classList.contains('e-readonly')));
            }");
        Assert.True(result, "Expected the control to be disabled or read-only.");
    }

    public static async Task FocusIsInsideFieldAsync(IPage page, string labelText)
    {
        await Assertions.Expect(page.Locator(".hr-field:focus-within").First)
            .ToContainTextAsync(labelText, new() { Timeout = 10_000 });
    }

    public static async Task<string> ActiveDescriptorAsync(IPage page) =>
        await page.EvaluateAsync<string>(
            @"() => {
                const el = document.activeElement;
                if (!el) return '';
                const container = el.closest('.hr-field, [class*=col-]');
                const labelFor = container?.querySelector('label[for]')?.getAttribute('for');
                const text = el.tagName === 'BUTTON' ? el.innerText.trim() : null;
                return [el.getAttribute('data-testid'), el.id, el.getAttribute('aria-label'), labelFor, text]
                    .filter(Boolean).join('|');
            }");

    public static async Task TabReachesInOrderAsync(IPage page, ILocator start, IReadOnlyList<string> expected, int maxTabs = 40)
    {
        await start.FocusAsync();
        var next = 0;
        var seen = new List<string>();
        for (var i = 0; i < maxTabs && next < expected.Count; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            var descriptor = await ActiveDescriptorAsync(page);
            seen.Add(descriptor);
            if (descriptor.Split('|').Contains(expected[next]))
                next++;
        }

        Assert.True(next == expected.Count,
            $"Tab order did not reach '{(next < expected.Count ? expected[next] : "")}' in the expected sequence. Visited: {string.Join(" > ", seen)}");
    }
}
