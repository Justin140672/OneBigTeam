using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Backfill Employee Numbers" dialog (BackfillEmployeeNumbersDialog.razor), reachable
/// from the "Employee Numbering" section of the standalone HR Settings page
/// (/companies/{id}/hr-settings — see HrSettingsPage.razor) only while the company's
/// employee-numbering mode is Automatic.
///
/// Employee Numbering (and this feature's entry-point button) used to live on the Company
/// Settings tab, gated behind Session.CanManageCompany (CompanyAdministrator-only). It has since
/// moved to the standalone HR Settings page, gated behind Session.IsHrAdministrator — which
/// matches the "employee:manage" policy guarding the backend preview/commit endpoints
/// (PreviewBackfillEmployeeNumbers/Endpoint.cs, CommitBackfillEmployeeNumbers/Endpoint.cs), so the
/// persona/policy mismatch previously documented here (CompanyAdministrator could open the button
/// but not call the endpoints behind it) no longer applies — Laura Bennett (HrAdministrator) can
/// do both.
/// </summary>
public sealed class BackfillEmployeeNumbersTests(HrSettingsSerialFixture fixture) : HrSettingsSerialTestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task BackfillButton_IsVisible_WhenNumberingModeIsAutomatic()
    {
        var login      = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await hrSettings.GoToAsync(AcmeId);

        var initialMode = await hrSettings.GetEmployeeNumberModeAsync();

        try
        {
            await hrSettings.SelectEmployeeNumberModeAsync("Automatic");

            Assert.True(await hrSettings.IsBackfillEmployeeNumbersButtonVisibleAsync(),
                "Expected the 'Backfill Employee Numbers…' button to be visible while Numbering Mode is Automatic");
        }
        finally
        {
            // Restore the original mode so this test doesn't leak state into other tests/fixtures.
            await hrSettings.SelectEmployeeNumberModeAsync(initialMode);
            await hrSettings.SaveAsync();
        }
    }

    [Fact]
    public async Task BackfillButton_IsNotVisible_WhenNumberingModeIsManual()
    {
        var login      = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await hrSettings.GoToAsync(AcmeId);

        var initialMode = await hrSettings.GetEmployeeNumberModeAsync();

        try
        {
            await hrSettings.SelectEmployeeNumberModeAsync("Manual");

            Assert.False(await hrSettings.IsBackfillEmployeeNumbersButtonVisibleAsync(),
                "Did not expect the 'Backfill Employee Numbers…' button to be visible while Numbering Mode is Manual");
        }
        finally
        {
            // Restore the original mode so this test doesn't leak state into other tests/fixtures.
            await hrSettings.SelectEmployeeNumberModeAsync(initialMode);
            await hrSettings.SaveAsync();
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
        var login      = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);
        var dialog     = new BackfillEmployeeNumbersDialogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await hrSettings.GoToAsync(AcmeId);

        var initialMode = await hrSettings.GetEmployeeNumberModeAsync();

        try
        {
            await hrSettings.SelectEmployeeNumberModeAsync("Automatic");
            await hrSettings.SaveAsync();
            Assert.False(await hrSettings.HasErrorAsync(),
                "Expected no error after switching the company's numbering mode to Automatic");

            // Re-navigate so the dialog is opened against the freshly-saved Automatic mode.
            await hrSettings.GoToAsync(AcmeId);

            await hrSettings.OpenBackfillEmployeeNumbersDialogAsync();
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
            await hrSettings.GoToAsync(AcmeId);
            await hrSettings.SelectEmployeeNumberModeAsync(initialMode);
            await hrSettings.SaveAsync();
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
