using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Internal recruitment Ticket 7 — completing an INTERNAL application by appointing the existing
/// employee from the vacancy Applications tab (VacancyApplicationsTab.razor: toolbar item
/// "app-appoint", the "Complete internal appointment" dialog, and the appoint-success banner;
/// backend POST .../applications/{id}/appoint).
///
/// Isolation: every test arranges its OWN data through the real HR.Api —
///   • a brand-new, GUID-titled, internally advertised Open vacancy on a brand-new position profile
///     (InternalVacancyApplyApi.CreateOpenInternalVacancyAsync);
///   • a brand-new Active employee (reporting to James, on the seeded QA Engineer profile) with a
///     freshly provisioned login, who applies to that vacancy as themselves via the employee
///     self-apply endpoint — a genuine internal application (Source == Internal);
///   • where needed, an external candidate application on the same vacancy, or a brand-new Active
///     employee to pick as the new manager.
/// Only seeded REFERENCE data is read (Acme, the seeded department/location/profile the fresh
/// employee starts on, James as the vacancy's hiring manager, the recruitment stage names).
/// Every grid assertion is scoped to this test's own unique names/ids.
///
/// Who drives the UI: the appoint endpoint requires recruitment:manage only, so by default the UI
/// session is this class's seeded Recruiter persona (Marcus — recruitment:manage, no employee:manage),
/// for whom the success banner names the employee instead of linking to their full HR record. The
/// tests that go on to FOLLOW the profile link sign in instead as the dedicated HR Administrator +
/// Recruiter "appointer" (InternalAppointmentApi.EnsureAppointerAsync, created once per process and
/// never mutated). Employee-record checks always go through the HR Administrator API client.
/// </summary>
public sealed class InternalAppointmentTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = InternalVacancyApplyApi.AcmeId;

    // The fresh applicant's pre-appointment manager (InternalVacancyApplyApi) and the vacancy's
    // hiring manager — the dialog pre-selects the hiring manager.
    private const string JamesFullName = "James Okafor";

    // RecruitmentStageSeeder.BuildDefaultStages.
    private const string InitialStage = "Application Received";
    private const string HiredStage = "Hired";

    private sealed class Arranged(
        HttpClient hrAdminApi,
        HttpClient recruiterApi,
        InternalVacancyApplyApi.FreshVacancy vacancy,
        InternalAppointmentApi.VacancySnapshot vacancyDetail,
        InternalVacancyApplyApi.FreshEmployee applicant,
        InternalAppointmentApi.EmployeeSnapshot applicantBefore,
        Guid internalApplicationId,
        Guid? externalApplicationId,
        string? externalLastName) : IDisposable
    {
        public HttpClient HrAdminApi { get; } = hrAdminApi;
        public HttpClient RecruiterApi { get; } = recruiterApi;
        public InternalVacancyApplyApi.FreshVacancy Vacancy { get; } = vacancy;
        public InternalAppointmentApi.VacancySnapshot VacancyDetail { get; } = vacancyDetail;
        public InternalVacancyApplyApi.FreshEmployee Applicant { get; } = applicant;
        public InternalAppointmentApi.EmployeeSnapshot ApplicantBefore { get; } = applicantBefore;
        public Guid InternalApplicationId { get; } = internalApplicationId;
        public Guid? ExternalApplicationId { get; } = externalApplicationId;
        public string? ExternalLastName { get; } = externalLastName;

        public string NewPositionProfileTitle => VacancyDetail.PositionProfileTitle
            ?? throw new InvalidOperationException("The arranged vacancy has no position profile title.");

        public void Dispose()
        {
            HrAdminApi.Dispose();
            RecruiterApi.Dispose();
        }
    }

    /// <summary>
    /// Creates this test's own vacancy + internal application (and optionally an external one), then
    /// signs the browser in and opens the vacancy's Applications tab. The browser user is the seeded
    /// Recruiter (recruitment:manage only) unless <paramref name="withEmployeeProfileAccess"/> asks for
    /// the HR Administrator + Recruiter appointer, needed only to follow the banner's profile link.
    /// </summary>
    private async Task<(Arranged Arranged, VacancyDetailPage VacancyDetail)> ArrangeAsync(
        bool withExternalApplication = false, bool withEmployeeProfileAccess = false)
    {
        var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);

        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);
        var vacancyDetail = await InternalAppointmentApi.GetVacancyAsync(recruiterApi, vacancy.Id);

        InternalVacancyApplyApi.FreshEmployee applicant;
        InternalVacancyApplyApi.InternalApplication internalApplication;
        var browserUserEmail = withEmployeeProfileAccess
            ? (await InternalAppointmentApi.EnsureAppointerAsync(hrAdminApi, _fixture.ApiBaseUrl)).WorkEmail
            : InternalAppointmentApi.RecruiterEmail;

        applicant = await InternalVacancyApplyApi.CreateActiveEmployeeWithLoginAsync(hrAdminApi, _fixture.ApiBaseUrl);
        using (var employeeApi = await InternalVacancyApplyApi.CreateEmployeeApiClientAsync(_fixture.ApiBaseUrl, applicant.WorkEmail))
        {
            internalApplication = await InternalVacancyApplyApi.ApplyAsEmployeeAsync(
                employeeApi, vacancy.Id, $"cv-{applicant.LastName}.pdf");
        }

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(browserUserEmail);

        Guid? externalApplicationId = null;
        string? externalLastName = null;
        if (withExternalApplication)
        {
            var unique = Guid.NewGuid().ToString("N")[..8];
            externalLastName = $"External{unique}";
            var externalCandidateId = await CandidateCvApi.CreateCandidateAsync(
                recruiterApi, AcmeId, "E2E", externalLastName, $"e2e.ext.{unique}@example.com");
            externalApplicationId = await CandidateCvApi.CreateApplicationAsync(
                recruiterApi, AcmeId, vacancy.Id, externalCandidateId);
        }

        var applicantBefore = await InternalAppointmentApi.GetEmployeeAsync(hrAdminApi, applicant.Id);
        Assert.NotEqual(vacancyDetail.PositionProfileId, applicantBefore.PositionProfileId);

        var arranged = new Arranged(
            hrAdminApi, recruiterApi, vacancy, vacancyDetail, applicant, applicantBefore,
            internalApplication.ApplicationId, externalApplicationId, externalLastName);

        var page = new VacancyDetailPage(_page, _fixture.WebBaseUrl);
        await page.GoToAsync(AcmeId, vacancy.Id);
        await page.OpenApplicationsTabAsync();
        await page.ExpectApplicationRowInternalAsync(arranged.InternalApplicationId, isInternal: true);

        return (arranged, page);
    }

    private async Task<InternalAppointmentDialog> OpenAppointDialogAsync(VacancyDetailPage vacancyDetail, Arranged arranged)
    {
        await vacancyDetail.ClickAppointForAsync(arranged.Applicant.LastName);
        var dialog = new InternalAppointmentDialog(_page);
        await dialog.WaitForOpenAsync();
        return dialog;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    // ── Employee profile helpers (the page the success banner links to) ──────────────

    private Task ExpectProfileHeaderAsync(string fullName) =>
        Assertions.Expect(_page.Locator("h1").Filter(new() { HasText = fullName }).First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

    private ILocator ReportsToLine => _page.Locator("p").Filter(new() { HasText = "Reports To:" });

    private async Task ExpectEmploymentTabPositionProfileAsync(string positionProfileTitle)
    {
        var employee = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        await employee.OpenEmploymentTabAsync();
        // The Organisation card's column whose form label is exactly "Position Profile *"
        // (EmployeeEmploymentTab.razor) — not any column that merely mentions the phrase.
        var positionInput = _page.Locator(".col-md-4")
            .Filter(new()
            {
                Has = _page.Locator("label.form-label", new()
                {
                    HasTextRegex = new System.Text.RegularExpressions.Regex(@"^\s*Position Profile\s*\*?\s*$"),
                }),
            })
            .First
            .Locator("span[role='combobox'] input").First;
        await Assertions.Expect(positionInput).ToHaveValueAsync(positionProfileTitle, new() { Timeout = 20_000 });
    }

    // ── 1. Appoint vs Hire ────────────────────────────────────────────────────────

    [Fact]
    public async Task Toolbar_InternalRowEnablesAppointNotHire_ExternalRowOnSameVacancyEnablesHireNotAppoint()
    {
        var (arranged, vacancyDetail) = await ArrangeAsync(withExternalApplication: true);
        using var _ = arranged;

        await vacancyDetail.ExpectApplicationRowTotalAsync(2);
        await vacancyDetail.ExpectApplicationRowInternalAsync(arranged.ExternalApplicationId!.Value, isInternal: false);
        await vacancyDetail.ExpectToolbarTooltipAsync("Complete internal appointment");

        // A fresh internal application is not mid-appointment.
        await vacancyDetail.ExpectAppointmentPendingHintAsync(arranged.InternalApplicationId, visible: false);

        // Internal row: Appoint enabled, Hire disabled (Hire's enable-state is applied before
        // Appoint's in RefreshToolbarStateAsync, so it has settled once Appoint is enabled).
        await vacancyDetail.ExpectToolbarItemEnabledForRowAsync(arranged.Applicant.LastName, "Appoint");
        await vacancyDetail.ExpectToolbarItemDisabledAsync("Hire");

        // External row on the same vacancy: the reverse. Appoint going from enabled to disabled is a
        // real transition, not the toolbar's initial no-selection state.
        await vacancyDetail.ExpectToolbarItemEnabledForRowAsync(arranged.ExternalLastName!, "Hire");
        await vacancyDetail.ExpectToolbarItemDisabledAsync("Appoint");
    }

    // ── 2. Dialog context ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Dialog_ShowsExistingEmployeeNotice_AndVacancyDerivedProfileDepartmentAndLocation()
    {
        var (arranged, vacancyDetail) = await ArrangeAsync();
        using var _ = arranged;

        var departmentId = arranged.VacancyDetail.PositionProfileDepartmentId
            ?? throw new InvalidOperationException("The arranged vacancy's position profile has no department.");
        var departmentName = await InternalAppointmentApi.GetDepartmentNameAsync(arranged.HrAdminApi, departmentId);
        var locationName = arranged.VacancyDetail.EffectiveLocation
            ?? throw new InvalidOperationException("The arranged vacancy has no effective location.");

        var dialog = await OpenAppointDialogAsync(vacancyDetail, arranged);

        await dialog.ExpectExistingEmployeeNoticeAsync(arranged.Applicant.FullName);
        await dialog.ExpectDerivedFieldsAsync(arranged.NewPositionProfileTitle, departmentName, locationName);
        await dialog.ExpectOnlyManagerComboboxAsync();

        // Defaults: effective today (no offer), hiring manager pre-selected, no date-specific UI.
        await dialog.ExpectEffectiveDateAsync(Today);
        await dialog.ExpectManagerAsync(JamesFullName);
        await dialog.ExpectFutureDateHintAsync(visible: false);
        await dialog.ExpectBackdatedConfirmationAsync(visible: false);
        await dialog.ExpectCompensationFieldsHiddenAsync();

        await dialog.CancelAsync();
    }

    // ── 3. Appoint with a selected manager, effective today ──────────────────────

    [Fact]
    public async Task Appoint_WithSelectedManagerToday_UpdatesExistingEmployee_LinksToProfile_AndMovesApplicationToHired()
    {
        // This test's own new manager — never a shared seeded persona.
        var newManager = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "AppointMgr", activate: true);

        var (arranged, vacancyDetail) = await ArrangeAsync(withEmployeeProfileAccess: true);
        using var _ = arranged;
        var applicant = arranged.Applicant;

        var dialog = await OpenAppointDialogAsync(vacancyDetail, arranged);
        await dialog.ExpectEffectiveDateAsync(Today);
        await dialog.SelectManagerAsync(newManager.LastName);
        await dialog.ExpectManagerAsync(newManager.FullName);
        await dialog.SubmitExpectingSuccessAsync();

        await dialog.ExpectAppliedSuccessBannerAsync();
        await dialog.ExpectEmployeeLinkAsync(AcmeId, applicant.Id);

        // The application row reloads onto the Hired terminal stage, not left mid-appointment.
        await vacancyDetail.ExpectApplicationStatusAsync(applicant.LastName, HiredStage);
        await vacancyDetail.ExpectAppointmentPendingHintAsync(arranged.InternalApplicationId, visible: false);

        // The EXISTING employee record was updated — and no second employee was created for them.
        var after = await InternalAppointmentApi.GetEmployeeAsync(arranged.HrAdminApi, applicant.Id);
        Assert.Equal(arranged.VacancyDetail.PositionProfileId, after.PositionProfileId);
        Assert.Equal(newManager.Id, after.ManagerId);
        Assert.Equal(1, await InternalAppointmentApi.CountEmployeesMatchingAsync(arranged.HrAdminApi, applicant.LastName));

        // Following the link opens that same employee's profile showing the new position/manager.
        await dialog.FollowEmployeeLinkAsync(applicant.Id);
        await ExpectProfileHeaderAsync(applicant.FullName);
        await Assertions.Expect(ReportsToLine.First).ToContainTextAsync(newManager.FullName, new() { Timeout = 15_000 });
        await ExpectEmploymentTabPositionProfileAsync(arranged.NewPositionProfileTitle);
    }

    // ── 4. "No manager" ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Appoint_WithNoManager_CompletesAndProfileShowsNoManager()
    {
        var (arranged, vacancyDetail) = await ArrangeAsync(withEmployeeProfileAccess: true);
        using var _ = arranged;
        var applicant = arranged.Applicant;

        // Before: the applicant reports to James (so "no manager" is a real change).
        Assert.NotNull(arranged.ApplicantBefore.ManagerId);

        var dialog = await OpenAppointDialogAsync(vacancyDetail, arranged);
        await dialog.SelectManagerAsync("No manager");
        await dialog.ExpectManagerAsync("No manager");
        await dialog.SubmitExpectingSuccessAsync();

        await dialog.ExpectAppliedSuccessBannerAsync();
        await vacancyDetail.ExpectApplicationStatusAsync(applicant.LastName, HiredStage);

        var after = await InternalAppointmentApi.GetEmployeeAsync(arranged.HrAdminApi, applicant.Id);
        Assert.Null(after.ManagerId);
        Assert.Equal(arranged.VacancyDetail.PositionProfileId, after.PositionProfileId);

        await dialog.FollowEmployeeLinkAsync(applicant.Id);
        await ExpectProfileHeaderAsync(applicant.FullName);
        // Header rendered from the same _employee load as the "Reports To:" line, so its absence
        // here is the post-load state, not a not-yet-rendered one.
        await Assertions.Expect(ReportsToLine).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await ExpectEmploymentTabPositionProfileAsync(arranged.NewPositionProfileTitle);
    }

    // ── 5. Future effective date ──────────────────────────────────────────────────

    [Fact]
    public async Task Appoint_FutureEffectiveDate_ShowsHint_BannerMentionsDate_AndEmployeeUnchangedUntilThen()
    {
        var (arranged, vacancyDetail) = await ArrangeAsync();
        using var _ = arranged;
        var applicant = arranged.Applicant;
        var effectiveDate = Today.AddDays(14);

        var dialog = await OpenAppointDialogAsync(vacancyDetail, arranged);
        await dialog.ExpectFutureDateHintAsync(visible: false);

        await dialog.SetEffectiveDateAsync(effectiveDate);
        await dialog.ExpectFutureDateHintAsync(visible: true);
        await dialog.ExpectBackdatedConfirmationAsync(visible: false);

        await dialog.SubmitExpectingSuccessAsync();
        await dialog.ExpectScheduledSuccessBannerAsync(effectiveDate);
        // The Recruiter cannot open the full employee record: the banner names the employee, no link.
        await dialog.ExpectEmployeeNameWithoutLinkAsync(applicant.FullName);

        // Scheduled, not applied: the employee keeps their current position and manager until then.
        var after = await InternalAppointmentApi.GetEmployeeAsync(arranged.HrAdminApi, applicant.Id);
        Assert.Equal(arranged.ApplicantBefore.PositionProfileId, after.PositionProfileId);
        Assert.NotEqual(arranged.VacancyDetail.PositionProfileId, after.PositionProfileId);
        Assert.Equal(arranged.ApplicantBefore.ManagerId, after.ManagerId);
        Assert.Equal(1, await InternalAppointmentApi.CountEmployeesMatchingAsync(arranged.HrAdminApi, applicant.LastName));
    }

    // ── 6. Past effective date needs confirmation ────────────────────────────────

    [Fact]
    public async Task Appoint_PastEffectiveDate_RequiresBackdatedConfirmation_ThenAppliesImmediately()
    {
        var (arranged, vacancyDetail) = await ArrangeAsync();
        using var _ = arranged;
        var applicant = arranged.Applicant;
        var effectiveDate = Today.AddDays(-7);

        var dialog = await OpenAppointDialogAsync(vacancyDetail, arranged);
        await dialog.ExpectBackdatedConfirmationAsync(visible: false);

        await dialog.SetEffectiveDateAsync(effectiveDate);
        await dialog.ExpectBackdatedConfirmationAsync(visible: true);
        await dialog.ExpectFutureDateHintAsync(visible: false);

        // Unconfirmed: blocked client-side, dialog stays open, nothing is changed.
        await dialog.SubmitExpectingErrorAsync("confirm the backdated appointment");
        await dialog.ExpectNoSuccessBannerAsync();
        var blocked = await InternalAppointmentApi.GetEmployeeAsync(arranged.HrAdminApi, applicant.Id);
        Assert.Equal(arranged.ApplicantBefore.PositionProfileId, blocked.PositionProfileId);

        // Confirmed: applied immediately (no "takes effect on" clause).
        await dialog.ConfirmBackdatedAsync();
        await dialog.SubmitExpectingSuccessAsync();
        await dialog.ExpectAppliedSuccessBannerAsync();
        await vacancyDetail.ExpectApplicationStatusAsync(applicant.LastName, HiredStage);

        var after = await InternalAppointmentApi.GetEmployeeAsync(arranged.HrAdminApi, applicant.Id);
        Assert.Equal(arranged.VacancyDetail.PositionProfileId, after.PositionProfileId);
    }

    // ── 7. Compensation change ───────────────────────────────────────────────────

    [Fact]
    public async Task Appoint_WithCompensationChange_RevealsFields_RequiresSalary_AndRecordsRoleChangeCompensation()
    {
        var (arranged, vacancyDetail) = await ArrangeAsync();
        using var _ = arranged;
        var applicant = arranged.Applicant;
        var notes = $"E2E appointment pay {applicant.LastName}";

        var dialog = await OpenAppointDialogAsync(vacancyDetail, arranged);
        await dialog.ExpectCompensationFieldsHiddenAsync();

        await dialog.SetChangeCompensationAsync(true);
        await dialog.ExpectCompensationFieldsVisibleAsync();

        // Salary is required once the compensation change is on (no offer → no prefilled salary).
        await dialog.SubmitExpectingErrorAsync("Please enter a salary greater than zero.");

        await dialog.FillSalaryAsync("42000");
        await dialog.FillCompensationNotesAsync(notes);
        await dialog.SubmitExpectingSuccessAsync();

        await dialog.ExpectAppliedSuccessBannerAsync();
        await vacancyDetail.ExpectApplicationStatusAsync(applicant.LastName, HiredStage);

        var compensation = await InternalAppointmentApi.GetCurrentCompensationAsync(arranged.HrAdminApi, applicant.Id);
        Assert.Equal(42000m, compensation.Salary);
        Assert.Equal("Annual", compensation.SalaryType);
        Assert.Equal("GBP", compensation.Currency);
        Assert.Equal(notes, compensation.Notes);
        Assert.Equal("RoleChange", compensation.Reason);
        Assert.Equal(Today, compensation.EffectiveFrom);

        var after = await InternalAppointmentApi.GetEmployeeAsync(arranged.HrAdminApi, applicant.Id);
        Assert.Equal(arranged.VacancyDetail.PositionProfileId, after.PositionProfileId);
    }

    // ── 8. Cancel ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_ClosesDialog_WithoutAppointing()
    {
        var (arranged, vacancyDetail) = await ArrangeAsync();
        using var _ = arranged;
        var applicant = arranged.Applicant;

        var dialog = await OpenAppointDialogAsync(vacancyDetail, arranged);
        // Make an edit first so Cancel demonstrably discards it rather than there being nothing to lose.
        await dialog.SelectManagerAsync("No manager");
        await dialog.CancelAsync();

        await dialog.ExpectNoSuccessBannerAsync();
        await vacancyDetail.ExpectApplicationStatusAsync(applicant.LastName, InitialStage);
        await vacancyDetail.ExpectAppointmentPendingHintAsync(arranged.InternalApplicationId, visible: false);

        var after = await InternalAppointmentApi.GetEmployeeAsync(arranged.HrAdminApi, applicant.Id);
        Assert.Equal(arranged.ApplicantBefore.PositionProfileId, after.PositionProfileId);
        Assert.Equal(arranged.ApplicantBefore.ManagerId, after.ManagerId);

        // The internal row can still be appointed afterwards.
        await vacancyDetail.ExpectToolbarItemEnabledForRowAsync(applicant.LastName, "Appoint");
    }
}
