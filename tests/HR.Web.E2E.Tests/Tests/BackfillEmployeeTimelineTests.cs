using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Backfill Employee Timeline" dialog (BackfillEmployeeTimelineDialog.razor),
/// reachable from the "Employee Timeline" subsection of the Company Settings tab
/// (CompanySettingsTab.razor). Unlike BackfillEmployeeNumbersDialog, this dialog has no preview
/// step — it's confirmation-only and always safe to re-run (backend dedupes by design).
///
/// IMPORTANT — two gaps discovered while writing these tests, mirroring the ones documented in
/// BackfillEmployeeNumbersTests:
///
/// 1. Persona/policy gap: the Settings tab itself is only reachable while Session.CanManageCompany
///    is true (the "company:manage" policy, restricted to CompanyAdministrator — see
///    CompanyEdit.razor). But the "Backfill Employee Timeline…" button is separately gated behind
///    Session.CanManageEmployees (mirroring "employee:manage", restricted to HrAdministrator — see
///    CompanySettingsTab.razor). As with the numbers-backfill feature, no persona seeded by
///    IdentityModule.SeedDevPersonasAsync holds both roles simultaneously (Priya Shah has
///    CompanyAdministrator only; Laura Bennett/David Park have HrAdministrator only). So there is no
///    single logged-in user who can both open the Settings tab and see the timeline-backfill button.
///    The tests below use Priya Shah (the only persona that can reach the Settings tab at all) to
///    verify the button is genuinely absent for her, and document — rather than force — the "visible
///    for an HR administrator" positive case, which would require either a persona with both roles or
///    relaxing one of the two policies.
///
/// 2. Nesting gap (RESOLVED): the "Employee Timeline" subsection was originally rendered inside
///    CompanySettingsTab.razor's `@if (Model.EmployeeNumberMode == EmployeeNumberMode.Automatic)`
///    block alongside the Employee Numbering fields. This has since been corrected so the section
///    (and its button) renders independently of Numbering Mode — Employee Numbering has since
///    moved entirely off the Settings tab to the standalone HR Settings page (see
///    HrSettingsPage.razor), so there's no longer any Numbering Mode state on this tab to even
///    couple to.
/// </summary>
public sealed class BackfillEmployeeTimelineTests(PriyaShahPersonaFixture fixture)
    : RoleE2ETestBase<PriyaShahPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Only a CompanyAdministrator can open the Settings tab at all (see class doc comment).
    private const string CompanyAdminEmail = "priya.shah@acme.example";

    /// <summary>
    /// Priya Shah (CompanyAdministrator only) can reach the Settings tab but does not hold
    /// Session.CanManageEmployees, so the "Backfill Employee Timeline…" button must not render for
    /// her — this is the one half of the permission gating that's actually verifiable given the
    /// current seed data (see class doc comment, gap 1).
    /// </summary>
    [Fact]
    public async Task BackfillTimelineButton_IsNotVisible_ForCompanyAdministratorWithoutEmployeeManagePermission()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var companyEdit = new CompanyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await companyEdit.GoToAsync(AcmeId);
        await companyEdit.OpenSettingsTabAsync();

        Assert.False(await companyEdit.IsBackfillEmployeeTimelineButtonVisibleAsync(),
            "Did not expect the 'Backfill Employee Timeline…' button to be visible for a " +
            "CompanyAdministrator-only persona (Session.CanManageEmployees should be false)");
    }

    // ── Coverage gaps (documented rather than forced) ───────────────────────────
    //
    // 1. "Trigger visible/enabled for an HR administrator" and "clicking it shows the confirmation
    //    dialog" / "confirming shows the success summary" / "cancelling closes without committing":
    //    all of these require a persona that can both reach the Settings tab (Session.CanManageCompany)
    //    and see the button (Session.CanManageEmployees). No seeded persona satisfies both (see class
    //    doc comment, gap 1), so these scenarios cannot currently be exercised end-to-end through the
    //    real UI. Once a persona with both roles exists (or the policies are reconciled — this mirrors
    //    the exact same open question already on file for BackfillEmployeeNumbersTests), the following
    //    should be added, using BackfillEmployeeTimelineDialogPage:
    //
    //      var dialog = new BackfillEmployeeTimelineDialogPage(_page, _fixture.WebBaseUrl);
    //      await companyEdit.OpenBackfillEmployeeTimelineDialogAsync();
    //      await dialog.WaitForVisibleAsync();
    //      Assert.True(await dialog.HasConfirmationTextAsync());
    //      Assert.True(await dialog.IsConfirmButtonVisibleAsync());
    //
    //      // Confirm path:
    //      await dialog.ConfirmAsync();
    //      Assert.True(await dialog.HasSuccessSummaryAsync());
    //      Assert.Matches(@"Timeline backfill complete: \d+ created, \d+ skipped, \d+ failed\.",
    //          await dialog.GetSuccessSummaryTextAsync() ?? string.Empty);
    //
    //      // Cancel path (opened again from a fresh dialog instance):
    //      await companyEdit.OpenBackfillEmployeeTimelineDialogAsync();
    //      await dialog.WaitForVisibleAsync();
    //      await dialog.CancelAsync();
    //      Assert.False(await dialog.IsVisibleAsync());
    //
    // 2. The Numbering-Mode nesting gap (see class doc comment, gap 2) has been resolved — the
    //    "Employee Timeline" subsection was moved out of the `EmployeeNumberMode == Automatic` block
    //    in CompanySettingsTab.razor and is now gated only on Session.CanManageEmployees. Employee
    //    Numbering itself has since moved off this tab entirely (see HrSettingsPage.razor).
}
