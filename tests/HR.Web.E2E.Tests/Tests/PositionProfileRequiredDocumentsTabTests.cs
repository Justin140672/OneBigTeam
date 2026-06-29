using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Required Documents tab on the Position Profile edit page.
///
/// Uses the seeded "Software Engineer" profile
/// (ID: 20000000-0000-0000-0000-000000000003) from Acme Corporation,
/// which has no required documents in the seed data.
/// </summary>
[Collection("E2E")]
public sealed class PositionProfileRequiredDocumentsTabTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId            = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SoftwareEngineerId = Guid.Parse("20000000-0000-0000-0000-000000000003");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task RequiredDocumentsTab_IsVisible_When_EditingExistingProfile()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppEdit.GoToAsync(AcmeId, SoftwareEngineerId);

        Assert.True(
            await ppEdit.HasRequiredDocumentsTabAsync(),
            "Expected a 'Required Documents' tab on the position profile edit page");
    }

    [Fact]
    public async Task RequiredDocumentsTab_IsNotVisible_When_CreatingNewProfile()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppEdit.GoToNewAsync(AcmeId);

        Assert.False(
            await ppEdit.HasRequiredDocumentsTabAsync(),
            "Expected no 'Required Documents' tab when creating a new position profile");
    }

    [Fact]
    public async Task RequiredDocumentsTab_ShowsAddButton_For_AuthorisedUser()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppEdit.GoToAsync(AcmeId, SoftwareEngineerId);
        await ppEdit.OpenRequiredDocumentsTabAsync();

        Assert.True(
            await _page.GetByRole(AriaRole.Button, new() { Name = "Add" }).IsVisibleAsync(),
            "Expected an 'Add' button on the Required Documents tab for an authorised user");
    }

    [Fact]
    public async Task RequiredDocumentsTab_CanAddAndRemove_RequiredDocument()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppEdit.GoToAsync(AcmeId, SoftwareEngineerId);
        await ppEdit.OpenRequiredDocumentsTabAsync();

        // ── Add ──────────────────────────────────────────────────────────────
        await ppEdit.ClickAddRequiredDocumentAsync();
        await ppEdit.SelectDocumentTypeInDialogAsync("Passport");
        await ppEdit.SubmitAddDialogAsync();

        // Verify it appears in the grid.
        await _page.WaitForSelectorAsync(".e-grid .e-row:has-text('Passport')", new() { Timeout = 10_000 });
        Assert.True(
            await ppEdit.HasRequiredDocumentInGridAsync("Passport"),
            "Expected 'Passport' to appear in the Required Documents grid after adding");

        // ── Remove ───────────────────────────────────────────────────────────
        await ppEdit.ClickRemoveRequiredDocumentAsync("Passport");
        await ppEdit.ConfirmRemoveAsync();

        // Wait for the row to disappear.
        await _page.WaitForFunctionAsync(
            "!document.querySelector('.e-grid .e-row') || " +
            "![...document.querySelectorAll('.e-grid .e-row')].some(r => r.textContent.includes('Passport'))",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        Assert.False(
            await ppEdit.HasRequiredDocumentInGridAsync("Passport"),
            "Expected 'Passport' to be removed from the Required Documents grid");
    }
}
