using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that an employee can manage their own emergency contacts
/// from the self-service My Profile page.
/// </summary>
[Collection("E2E")]
public sealed class EmergencyContactsTabTests : IAsyncLifetime
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId  = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    private readonly AppFixture _fixture;
    private IBrowserContext _context = null!;
    private IPage           _page    = null!;

    public EmergencyContactsTabTests(AppFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync();
        _page    = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task EmergencyContactsTab_AddContact_AppearsInList()
    {
        // Use unique phone to distinguish this test's contact from any pre-existing ones.
        var unique       = Guid.NewGuid().ToString("N")[..8];
        var contactName  = $"E2E Contact {unique}";
        var contactPhone = "07700 900099";

        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile   = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var emergency = new EmergencyContactsTab(_page);

        // ── Step 1: Login as Tom ──────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        // ── Step 2: Navigate to Emergency Contacts tab ────────────────────────
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenEmergencyContactsTabAsync();
        await emergency.WaitForLoadAsync();

        // ── Step 3: Add a new contact ────────────────────────────────────────
        await emergency.ClickAddContactAsync();
        await emergency.FillContactNameAsync(contactName);
        await emergency.FillContactRelationshipAsync("Friend");
        await emergency.FillContactPhoneAsync(contactPhone);
        await emergency.SaveContactAsync();

        // ── Step 4: Success banner confirms the save ──────────────────────────
        Assert.True(await emergency.IsSuccessBannerVisibleAsync(),
            "Expected a success banner after adding an emergency contact");

        // ── Step 5: The new contact appears in the list ───────────────────────
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

        // The tab should be accessible — either showing contacts or the empty state.
        var content = await _page.ContentAsync();
        var isRendered = await _page.Locator(".ec-card").First.IsVisibleAsync();

        Assert.True(isRendered,
            "Expected the Emergency Contacts tab to render a card for the employee's own profile");
    }
}
