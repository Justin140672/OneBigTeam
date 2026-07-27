using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Backfill Employee Numbers" dialog (BackfillEmployeeNumbersDialog.razor), reachable
/// from the "Employee Numbering" section of the Company Settings tab only while the company's
/// employee-numbering mode is Automatic (see CompanySettingsTab.razor).
///
/// IMPORTANT — persona/policy gap discovered while writing these tests: the Settings tab (and
/// therefore this feature's entry-point button) is gated behind Session.CanManageCompany, which the
/// "company:manage" policy restricts to CompanyAdministrator only (see CompanyEdit.razor,
/// IdentityModule.AddRolePolicies). But the backend preview/commit endpoints
/// (PreviewBackfillEmployeeNumbers/Endpoint.cs, CommitBackfillEmployeeNumbers/Endpoint.cs) are both
/// gated behind "employee:manage", which that same policy table restricts to HrAdministrator only —
/// and no persona seeded by IdentityModule.SeedDevPersonasAsync holds both roles simultaneously
/// (Priya Shah has CompanyAdministrator only; Laura Bennett/David Park have HrAdministrator only).
/// So with the current seed data there is no single logged-in user who can both see the "Backfill
/// Employee Numbers…" button and successfully call the preview/commit endpoints behind it — the
/// dialog-opening tests below use Priya Shah (the only persona that can reach the Settings tab at
/// all) and may need re-pointing at a persona with both roles if one is added to the seed data, or
/// may surface this as a genuine 403 in the dialog's error banner until the policies/role
/// assignment are reconciled. This is a product/seed-data question, not something addressed here —
/// see the class-level test names for what's actually independently verifiable regardless of that
/// question.
/// </summary>
[Collection("E2E")]
public sealed class BackfillEmployeeNumbersTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Only a CompanyAdministrator can open the Settings tab at all (see class doc comment).
    private const string CompanyAdminEmail = "priya.shah@acme.example";

    [Fact]
    public async Task BackfillButton_IsVisible_WhenNumberingModeIsAutomatic()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        var initialMode = await companyEdit.GetEmployeeNumberModeAsync();

        try
        {
            await companyEdit.SelectEmployeeNumberModeAsync("Automatic");

            Assert.True(await companyEdit.IsBackfillEmployeeNumbersButtonVisibleAsync(),
                "Expected the 'Backfill Employee Numbers…' button to be visible while Numbering Mode is Automatic");
        }
        finally
        {
            // Restore the original mode so this test doesn't leak state into other tests/fixtures.
            await companyEdit.SelectEmployeeNumberModeAsync(initialMode);
            await companyEdit.SaveAsync();
        }
    }

    [Fact]
    public async Task BackfillButton_IsNotVisible_WhenNumberingModeIsManual()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        var initialMode = await companyEdit.GetEmployeeNumberModeAsync();

        try
        {
            await companyEdit.SelectEmployeeNumberModeAsync("Manual");

            Assert.False(await companyEdit.IsBackfillEmployeeNumbersButtonVisibleAsync(),
                "Did not expect the 'Backfill Employee Numbers…' button to be visible while Numbering Mode is Manual");
        }
        finally
        {
            // Restore the original mode so this test doesn't leak state into other tests/fixtures.
            await companyEdit.SelectEmployeeNumberModeAsync(initialMode);
            await companyEdit.SaveAsync();
        }
    }

    /// <summary>
    /// Acme's seeded employees all already have an employee number (see
    /// EmployeesModule.SeedEmployeesAsync), and there is no UI-reachable way to produce an employee
    /// with a blank number — CreateEmployee/UpdateEmploymentDetails both require a non-empty
    /// employee number even in Manual mode (see their Validators), and the only way integration
    /// tests reproduce a "missing number" candidate is by writing directly to the database (see
    /// PreviewBackfillEmployeeNumbersEndpointTests.SeedEmployeeMissingNumberAsync's doc comment). So
    /// switching Acme to Automatic mode and opening the dialog reliably exercises the *zero
    /// candidates* empty state end-to-end through the real UI, without needing any special seeding.
    /// </summary>
    [Fact]
    public async Task BackfillDialog_WithNoCandidates_ShowsEmptyStateAndDisablesConfirm()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);
        var dialog      = new BackfillEmployeeNumbersDialogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        var initialMode = await companyEdit.GetEmployeeNumberModeAsync();

        try
        {
            await companyEdit.SelectEmployeeNumberModeAsync("Automatic");
            await companyEdit.SaveAsync();
            Assert.False(await companyEdit.HasErrorAsync(),
                "Expected no error after switching the company's numbering mode to Automatic");

            // Re-navigate so the dialog is opened against the freshly-saved Automatic mode.
            await companyEdit.GoToAsync(AcmeId);
            await companyEdit.OpenSettingsTabAsync();

            await companyEdit.OpenBackfillEmployeeNumbersDialogAsync();
            await dialog.WaitForPreviewLoadedAsync();

            Assert.False(await dialog.HasGlobalErrorAsync(),
                $"Expected no API error loading the preview, got: {await dialog.GetGlobalErrorTextAsync()}");
            Assert.True(await dialog.HasEmptyStateMessageAsync(),
                "Expected the 'no employees missing an employee number' empty-state message");
            Assert.Equal(0, await dialog.GetCandidateRowCountAsync());
            Assert.False(await dialog.IsConfirmButtonVisibleAsync(),
                "Did not expect a Confirm button to render when there are zero backfill candidates");

            await dialog.CloseAsync();
        }
        finally
        {
            // Restore the original mode so this test doesn't leak state into other tests/fixtures.
            await companyEdit.GoToAsync(AcmeId);
            await companyEdit.OpenSettingsTabAsync();
            await companyEdit.SelectEmployeeNumberModeAsync(initialMode);
            await companyEdit.SaveAsync();
        }
    }

    // ── Coverage gaps (documented rather than forced) ───────────────────────────
    //
    // 1. "Candidates present" happy path (preview grid populated, Confirm succeeds, success
    //    summary shown, Next Number/candidate list reflects the change): every employee reachable
    //    through the UI already has a non-blank employee number by the time it's created or saved
    //    (CreateEmployee/UpdateEmploymentDetails both require one), and the only way to produce an
    //    employee with a genuinely blank EmployeeNumber is a direct EF write, which
    //    HR.Web.E2E.Tests has no existing seeding pattern or fixture support for (unlike
    //    HR.Integration.Tests, which seeds directly via EmployeesDbContext — see
    //    PreviewBackfillEmployeeNumbersEndpointTests.SeedEmployeeMissingNumberAsync). Adding
    //    database-level seeding infrastructure to this E2E project was judged out of scope/too
    //    invasive for this task; this scenario remains covered at the integration-test level
    //    (PreviewBackfillEmployeeNumbersEndpointTests, CommitBackfillEmployeeNumbersEndpointTests)
    //    but not at the E2E level.
    //
    // 2. Wrong-mode 409 path: the "Backfill Employee Numbers…" button (and therefore the dialog)
    //    is only rendered while Numbering Mode is Automatic, so there's no UI-reachable path to
    //    open the dialog while the company is in Manual mode and hit the endpoints' 409. This is
    //    exercised at the integration level
    //    (PreviewBackfillEmployeeNumbersEndpointTests.Returns_Conflict_When_Company_Is_In_Manual_Mode)
    //    instead.
}
