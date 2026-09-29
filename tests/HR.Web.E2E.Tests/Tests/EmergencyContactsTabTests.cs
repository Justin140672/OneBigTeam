using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class EmergencyContactsTabTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task EmergencyContactsTab_AddContact_AppearsInList()
    {
        var unique       = Guid.NewGuid().ToString("N")[..8];
        var contactName  = $"E2E Contact {unique}";
        var contactPhone = "07700 900099";

        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile   = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var emergency = new EmergencyContactsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenEmergencyContactsTabAsync();
        await emergency.WaitForLoadAsync();

        await emergency.ClickAddContactAsync();

        Assert.True(await _page.Locator(".e-dialog").IsVisibleAsync(),
            "Expected the 'Add Contact' form to appear in a dialog");

        await emergency.FillContactNameAsync(contactName);
        await emergency.FillContactRelationshipAsync("Friend");
        await emergency.FillContactPhoneAsync(contactPhone);
        await emergency.SaveContactAsync();

        Assert.True(await emergency.IsSuccessBannerVisibleAsync(),
            "Expected a success banner after adding an emergency contact");

        Assert.True(await emergency.HasContactAsync(contactName),
            $"Expected the contact '{contactName}' to appear in the emergency contacts list");
    }

    [Fact]
    public async Task EmergencyContactsTab_IsRendered_ForOwnProfile()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile   = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var emergency = new EmergencyContactsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenEmergencyContactsTabAsync();
        await emergency.WaitForLoadAsync();

        var content = await _page.ContentAsync();
        var isRendered = await _page.Locator(".ec-card").First.IsVisibleAsync();

        Assert.True(isRendered,
            "Expected the Emergency Contacts tab to render a card for the employee's own profile");
    }

    [Fact]
    public async Task EmergencyContactsTab_InvalidPhoneNumber_ShowsValidationError()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile   = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var emergency = new EmergencyContactsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenEmergencyContactsTabAsync();
        await emergency.WaitForLoadAsync();

        await emergency.ClickAddContactAsync();
        await emergency.FillContactNameAsync("Invalid Phone Test");
        await emergency.FillContactRelationshipAsync("Friend");
        await emergency.FillContactPhoneAsync("not-a-phone-number");

        await emergency.ClickSaveContactAsync();

        Assert.True(await emergency.HasValidationMessageAsync(),
            "Expected a validation error for a phone number that matches neither mobile nor telephone format");
        Assert.False(await emergency.HasContactAsync("Invalid Phone Test"),
            "The contact should not have been saved with an invalid phone number");
    }

    [Fact]
    public async Task EmergencyContactsTab_EditContact_PersistsChanges()
    {
        var unique          = Guid.NewGuid().ToString("N")[..8];
        var originalName    = $"E2E Original {unique}";
        var updatedName     = $"E2E Updated {unique}";
        var updatedPhone    = "07700 900098";
        var updatedRelation = "Parent";

        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile   = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var emergency = new EmergencyContactsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenEmergencyContactsTabAsync();
        await emergency.WaitForLoadAsync();

        await emergency.ClickAddContactAsync();
        await emergency.FillContactNameAsync(originalName);
        await emergency.FillContactRelationshipAsync("Friend");
        await emergency.FillContactPhoneAsync("07700 900097");
        await emergency.SaveContactAsync();

        Assert.True(await emergency.HasContactAsync(originalName),
            $"Expected the contact '{originalName}' to appear in the emergency contacts list before editing it");

        await emergency.ClickEditContactAsync(originalName);
        await emergency.FillContactNameAsync(updatedName);
        await emergency.FillContactRelationshipAsync(updatedRelation);
        await emergency.FillContactPhoneAsync(updatedPhone);

        await _page.Locator("button.e-primary, button[type='submit']")
            .Filter(new() { HasText = "Save" })
            .Last
            .ClickAsync();
        await _page.WaitForSelectorAsync(".ec-success-banner", new() { Timeout = 15_000 });

        Assert.True(await emergency.HasContactAsync(updatedName),
            $"Expected the updated contact '{updatedName}' to appear in the emergency contacts list");
        Assert.False(await emergency.HasContactAsync(originalName),
            $"Expected the original contact name '{originalName}' to no longer appear after editing");

        var content = await _page.ContentAsync();
        Assert.Contains(updatedPhone, content);
        Assert.Contains(updatedRelation, content);
    }
}
