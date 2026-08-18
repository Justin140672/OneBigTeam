using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that an employee can view and update their contact details
/// from the self-service My Profile page.
/// </summary>
public sealed class ContactDetailsTabTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task ContactDetailsTab_IsAccessible_AndShowsWorkEmail()
    {
        var login          = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile        = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var contactDetails = new ContactDetailsTab(_page);

        // ── Step 1: Login as Tom ──────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        // ── Step 2: Navigate to the self-service profile ──────────────────────
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenContactDetailsTabAsync();
        await contactDetails.WaitForLoadAsync();

        // ── Step 3: Card is rendered ──────────────────────────────────────────
        Assert.True(await contactDetails.IsVisibleAsync(),
            "Expected the contact details card to be visible");

        // ── Step 4: Work email is shown as read-only ──────────────────────────
        var workEmail = await contactDetails.GetWorkEmailAsync();
        Assert.False(string.IsNullOrWhiteSpace(workEmail),
            "Expected the work email to be displayed on the Contact Details tab");
        Assert.Contains("tom.williams@", workEmail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContactDetailsTab_SaveChanges_ShowsSuccessBanner()
    {
        var login          = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile        = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var contactDetails = new ContactDetailsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenContactDetailsTabAsync();
        await contactDetails.WaitForLoadAsync();

        // ── Fill in required address fields (needed for the form to be valid) ──
        await contactDetails.FillAddressLine1Async("123 Test Street");
        await contactDetails.FillCityAsync("London");
        await contactDetails.FillPostCodeAsync("EC1A 1BB");
        await contactDetails.FillCountryAsync("United Kingdom");

        // ── Update the mobile phone number ────────────────────────────────────
        await contactDetails.FillMobilePhoneAsync("07700 900001");

        // ── Save ──────────────────────────────────────────────────────────────
        await contactDetails.SaveChangesAsync();

        // ── Success banner should appear ──────────────────────────────────────
        Assert.True(await contactDetails.IsSuccessBannerVisibleAsync(),
            "Expected a success banner after saving contact details");
    }

    [Fact]
    public async Task ContactDetailsTab_PersonalEmail_CanBeUpdated()
    {
        var uniqueEmail = $"e2e.{Guid.NewGuid():N}@personal.example.com";

        var login          = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile        = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var contactDetails = new ContactDetailsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenContactDetailsTabAsync();
        await contactDetails.WaitForLoadAsync();

        // Update the personal email.
        await contactDetails.FillPersonalEmailAsync(uniqueEmail);

        // Fill required address fields so the form validates.
        await contactDetails.FillAddressLine1Async("1 High Street");
        await contactDetails.FillCityAsync("Manchester");
        await contactDetails.FillPostCodeAsync("M1 1AE");
        await contactDetails.FillCountryAsync("United Kingdom");

        await contactDetails.SaveChangesAsync();

        Assert.True(await contactDetails.IsSuccessBannerVisibleAsync(),
            "Expected a success banner after updating the personal email");

        // Navigate away and back to verify the value was persisted.
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenContactDetailsTabAsync();
        await contactDetails.WaitForLoadAsync();

        var savedEmail = await contactDetails.GetPersonalEmailAsync();
        Assert.Equal(uniqueEmail, savedEmail, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContactDetailsTab_InvalidPostCode_ShowsValidationError()
    {
        var login          = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile        = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var contactDetails = new ContactDetailsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenContactDetailsTabAsync();
        await contactDetails.WaitForLoadAsync();

        await contactDetails.FillAddressLine1Async("123 Test Street");
        await contactDetails.FillCityAsync("London");
        await contactDetails.FillPostCodeAsync("not a postcode");
        await contactDetails.FillCountryAsync("United Kingdom");

        await contactDetails.ClickSaveAsync();

        Assert.True(await contactDetails.HasGlobalErrorAsync(),
            "Expected a validation error for an invalid postcode");
        Assert.False(await contactDetails.IsSuccessBannerVisibleAsync(),
            "Save should not have succeeded with an invalid postcode");
    }

    [Fact]
    public async Task ContactDetailsTab_InvalidMobilePhone_ShowsValidationError()
    {
        var login          = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile        = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var contactDetails = new ContactDetailsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenContactDetailsTabAsync();
        await contactDetails.WaitForLoadAsync();

        await contactDetails.FillAddressLine1Async("123 Test Street");
        await contactDetails.FillCityAsync("London");
        await contactDetails.FillPostCodeAsync("EC1A 1BB");
        await contactDetails.FillCountryAsync("United Kingdom");
        await contactDetails.FillMobilePhoneAsync("12345");

        await contactDetails.ClickSaveAsync();

        Assert.True(await contactDetails.HasGlobalErrorAsync(),
            "Expected a validation error for an invalid mobile number");
        Assert.False(await contactDetails.IsSuccessBannerVisibleAsync(),
            "Save should not have succeeded with an invalid mobile number");
    }

    [Fact]
    public async Task ContactDetailsTab_InvalidHomePhone_ShowsValidationError()
    {
        var login          = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile        = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var contactDetails = new ContactDetailsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenContactDetailsTabAsync();
        await contactDetails.WaitForLoadAsync();

        await contactDetails.FillAddressLine1Async("123 Test Street");
        await contactDetails.FillCityAsync("London");
        await contactDetails.FillPostCodeAsync("EC1A 1BB");
        await contactDetails.FillCountryAsync("United Kingdom");
        await contactDetails.FillHomePhoneAsync("abcdefg");

        await contactDetails.ClickSaveAsync();

        Assert.True(await contactDetails.HasGlobalErrorAsync(),
            "Expected a validation error for an invalid home phone number");
        Assert.False(await contactDetails.IsSuccessBannerVisibleAsync(),
            "Save should not have succeeded with an invalid home phone number");
    }
}
