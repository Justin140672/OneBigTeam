using Microsoft.Playwright;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class CompanyEditAccessibilityTests(PriyaShahPersonaFixture fixture) : RoleE2ETestBase<PriyaShahPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string CompanyAdminEmail = "priya.shah@acme.example";

    private async Task OpenCompanyEditAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);
        await companyEdit.GoToAsync(AcmeId);
    }

    [Theory]
    [InlineData("company-name", "Name", true)]
    [InlineData("addr-0-line1", "Address Line 1", true)]
    [InlineData("addr-0-line2", "Address Line 2", false)]
    [InlineData("addr-0-city", "Town/City", true)]
    [InlineData("addr-0-region", "County/Region", false)]
    [InlineData("addr-0-postcode", "Postcode", false)]
    public async Task Field_AccessibleNameComesFromLabel_AndRequiredStateIsExposed(string id, string label, bool required)
    {
        await OpenCompanyEditAsync();

        var input = _page.Locator($"input#{id}");
        await input.WaitForAsync();

        var ariaLabel = await input.GetAttributeAsync("aria-label");
        Assert.NotEqual("textbox", ariaLabel);
        Assert.Equal(label, ariaLabel);

        var ariaRequired = await input.GetAttributeAsync("aria-required");
        Assert.Equal(required ? "true" : null, ariaRequired);
    }

    [Fact]
    public async Task AddressGroups_AreDistinguishableByLegend()
    {
        await OpenCompanyEditAsync();

        await _page.Locator("fieldset legend", new() { HasText = "Registered Office" }).WaitForAsync();
        Assert.Equal(1, await _page.GetByRole(AriaRole.Group, new() { Name = "Registered Office" }).CountAsync());
    }

    [Fact]
    public async Task ClearingRequiredField_AndSaving_MarksFieldInvalidAndDescribedByError()
    {
        await OpenCompanyEditAsync();

        var line1 = _page.Locator("input#addr-0-line1");
        await line1.WaitForAsync();
        await line1.FillAsync(string.Empty);
        await line1.PressAsync("Tab");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await _page.Locator("#addr-0-line1-error .validation-message").WaitForAsync();
        Assert.Equal("true", await line1.GetAttributeAsync("aria-invalid"));
        Assert.Contains("addr-0-line1-error", await line1.GetAttributeAsync("aria-describedby") ?? string.Empty);
        Assert.Equal("alert", await _page.Locator("#addr-0-line1-error").GetAttributeAsync("role"));
    }
}
