using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Employee "Apply" experience on the Internal Vacancies page
/// (src/HR.Web/Components/Pages/Recruitment/InternalVacancies.razor →
/// POST /api/companies/{companyId}/internal-vacancies/{vacancyId}/applications).
///
/// Isolation: every test arranges its OWN data through the real HR.Api (InternalVacancyApplyApi) —
/// a brand-new Active employee with a freshly provisioned login (never a shared seeded persona such
/// as Tom) and a brand-new, GUID-titled, internally-advertised Open vacancy. The page's search box
/// narrows the list to that one vacancy before any card is opened.
///
/// Serialization (two gates, always acquired in this order: vacancy gate, then Supabase gate):
///   1. CrossUserVacancyTestBase.GateInstance — applying creates candidates/applications on Acme's
///      shared recruitment pipeline, which the other recruitment tests read by stage/position.
///   2. SupabaseAuthGate (via SupabaseAuthSerialEmployeeTestBase) — ensure-employee-login and the
///      UI login as a non-persona user are real, uncached Supabase calls.
/// No other class in this project holds both gates, so the fixed order cannot deadlock.
/// </summary>
public sealed class InternalVacancyApplyTests(EmployeePersonaFixture fixture) : SupabaseAuthSerialEmployeeTestBase(fixture)
{
    private static readonly Guid AcmeId = InternalVacancyApplyApi.AcmeId;

    private const string PdfMime = "application/pdf";
    private const long MaxCvBytes = 20 * 1024 * 1024;

    public override async Task InitializeAsync()
    {
        await CrossUserVacancyTestBase.GateInstance.WaitAsync();
        try
        {
            await base.InitializeAsync();
        }
        catch
        {
            CrossUserVacancyTestBase.GateInstance.Release();
            throw;
        }
    }

    public override async Task DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            CrossUserVacancyTestBase.GateInstance.Release();
        }
    }

    private sealed record Arranged(
        InternalVacancyApplyApi.FreshEmployee Employee,
        InternalVacancyApplyApi.FreshVacancy Vacancy,
        HttpClient RecruiterApi);

    /// <summary>
    /// Creates this test's own employee + vacancy via the API, logs in through the UI as that
    /// employee, and lands on the Internal Vacancies page searched down to the new vacancy.
    /// <paramref name="beforeLogin"/> runs after the data exists but before the employee signs in.
    /// </summary>
    private async Task<Arranged> ArrangeAsync(
        InternalVacanciesPage internalVacancies,
        Func<InternalVacancyApplyApi.FreshEmployee, HttpClient, Task>? beforeLogin = null)
    {
        using var hrAdminApi = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(_fixture.ApiBaseUrl);
        var recruiterApi = await CandidateCvApi.CreateRecruiterApiClientAsync(_fixture.ApiBaseUrl);

        var employee = await InternalVacancyApplyApi.CreateActiveEmployeeWithLoginAsync(hrAdminApi, _fixture.ApiBaseUrl);
        var vacancy = await InternalVacancyApplyApi.CreateOpenInternalVacancyAsync(hrAdminApi, recruiterApi);

        if (beforeLogin is not null)
            await beforeLogin(employee, recruiterApi);

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(employee.WorkEmail);

        await internalVacancies.GoToAsync(AcmeId);
        await internalVacancies.SearchAsync(vacancy.Title);

        return new Arranged(employee, vacancy, recruiterApi);
    }

    // ── 1. Read-only identity, CV required, Cancel creates nothing ─────────────────────────

    [Fact]
    public async Task ApplyForm_ShowsReadOnlyIdentity_RequiresCv_AndCancelCreatesNoApplication()
    {
        var page = new InternalVacanciesPage(_page, _fixture.WebBaseUrl);
        var arranged = await ArrangeAsync(page);
        var (employee, vacancy) = (arranged.Employee, arranged.Vacancy);
        using var recruiterApi = arranged.RecruiterApi;

        await page.OpenCardWithKeyboardAsync(vacancy.Title);
        await page.ClickApplyAsync();

        Assert.True(await page.WaitForFocusAsync("internal-apply-cv-input"),
            $"Expected focus on the CV input after clicking Apply, but it was on '{await page.ActiveElementTestIdAsync()}'.");

        var name = await page.GetApplicantNameAsync();
        Assert.Contains("E2E", name);
        Assert.Contains(employee.LastName, name);
        Assert.Equal(employee.WorkEmail, await page.GetApplicantEmailAsync(), ignoreCase: true);
        Assert.Equal(0, await page.CountEditableIdentityInputsAsync());

        // Submit with no file — client-side required-CV message, still on the form.
        await page.SubmitApplicationAsync();
        await page.WaitForCvErrorAsync("Please choose a CV file to upload.");
        Assert.True(await page.IsApplyFormVisibleAsync(), "Expected to remain on the apply form after a missing-CV submit.");

        // Choose a valid CV, then Cancel — nothing is uploaded or created.
        await page.SelectValidCvAsync($"cv-{employee.LastName}.pdf", CandidateCvApi.BuildTestPdf());
        await page.CancelApplyAsync();

        Assert.True(await page.IsDetailVisibleAsync(), "Expected the vacancy details after Cancel.");
        Assert.True(await page.IsApplyButtonVisibleAsync(), "Expected the Apply button to be back after Cancel.");
        Assert.True(await page.WaitForFocusAsync("internal-vacancy-apply"),
            $"Expected focus to return to the Apply button after Cancel, but it was on '{await page.ActiveElementTestIdAsync()}'.");

        var applications = await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id);
        Assert.Empty(applications);
    }

    // ── 2. Client-side file validation ─────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyForm_RejectsWrongTypeOversizedAndEmptyFiles()
    {
        var page = new InternalVacanciesPage(_page, _fixture.WebBaseUrl);
        var arranged = await ArrangeAsync(page);
        var vacancy = arranged.Vacancy;
        using var recruiterApi = arranged.RecruiterApi;

        await page.OpenCardAsync(vacancy.Title);
        await page.ClickApplyAsync();

        // Wrong type.
        await page.SelectCvAsync("cv.txt", "text/plain", "plain text CV"u8.ToArray());
        await page.WaitForCvErrorAsync("The CV must be a PDF, DOC or DOCX file.");
        Assert.False(await page.IsCvSelectedVisibleAsync(), "A rejected .txt file must not be shown as selected.");

        // Empty (0-byte) PDF.
        await page.SelectCvAsync("empty.pdf", PdfMime, []);
        await page.WaitForCvErrorAsync("The selected CV file is empty.");
        Assert.False(await page.IsCvSelectedVisibleAsync(), "A rejected empty file must not be shown as selected.");

        // Oversized PDF — exactly one byte over the 20 MB limit.
        var oversized = new byte[MaxCvBytes + 1];
        CandidateCvApi.BuildTestPdf().AsSpan(0, 5).CopyTo(oversized);
        await page.SelectCvAsync("huge.pdf", PdfMime, oversized);
        await page.WaitForCvErrorAsync("The CV file is larger than the 20 MB limit.");
        Assert.False(await page.IsCvSelectedVisibleAsync(), "A rejected oversized file must not be shown as selected.");

        Assert.True(await page.IsApplyFormVisibleAsync(), "Expected to remain on the apply form after rejected files.");

        var applications = await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id);
        Assert.Empty(applications);
    }

    // ── 3. Happy path + persistence ────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_WithPdfCv_ShowsSubmittedAndAppliedState_AndPersistsAfterReload()
    {
        var page = new InternalVacanciesPage(_page, _fixture.WebBaseUrl);
        var arranged = await ArrangeAsync(page);
        var (employee, vacancy) = (arranged.Employee, arranged.Vacancy);
        using var recruiterApi = arranged.RecruiterApi;

        // A plain employee (no recruitment permission) reaches the page without an access-denied redirect.
        Assert.Contains("/internal-vacancies", _page.Url);
        Assert.DoesNotContain("/access-denied", _page.Url);

        await page.OpenCardAsync(vacancy.Title);
        await page.ClickApplyAsync();
        await page.SelectValidCvAsync($"cv-{employee.LastName}.pdf", CandidateCvApi.BuildTestPdf());
        await page.SubmitApplicationAsync();

        Assert.True(await page.IsSuccessVisibleAsync(), "Expected the 'Application submitted' confirmation.");
        Assert.Contains("Application submitted", await page.GetSuccessTextAsync());
        Assert.True(await page.IsAppliedStateVisibleAsync(), "Expected the Applied status in the details.");
        Assert.True(await page.IsAppliedButtonDisabledAsync(), "Expected a disabled Applied footer button.");
        Assert.Equal(0, await page.ApplyButtonCountAsync());
        Assert.True(await page.WaitForFocusAsync("internal-apply-success"),
            $"Expected focus on the success confirmation, but it was on '{await page.ActiveElementTestIdAsync()}'.");

        await page.CloseDetailAsync();
        Assert.True(await page.HasAppliedBadgeAsync(vacancy.Title), "Expected the card to show the Applied badge.");

        // Reload: hasApplied now comes from the server.
        await page.GoToAsync(AcmeId);
        await page.SearchAsync(vacancy.Title);
        Assert.True(await page.HasAppliedBadgeAsync(vacancy.Title), "Expected the Applied badge to persist after reload.");

        await page.OpenCardAsync(vacancy.Title);
        Assert.True(await page.IsAppliedStateVisibleAsync(), "Expected the Applied status when reopening after reload.");
        Assert.True(await page.IsAppliedButtonDisabledAsync(), "Expected the disabled Applied button when reopening after reload.");
        Assert.Equal(0, await page.ApplyButtonCountAsync());

        var applications = await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id);
        var application = Assert.Single(applications);
        Assert.Equal(employee.WorkEmail, application.CandidateEmail, ignoreCase: true);
    }

    // ── 4. Duplicate submission from a second tab ──────────────────────────────────────────

    [Fact]
    public async Task Apply_WhenAlreadyAppliedInAnotherTab_ShowsFriendlyAlreadyAppliedMessage_AndAppliedState()
    {
        var page = new InternalVacanciesPage(_page, _fixture.WebBaseUrl);
        var arranged = await ArrangeAsync(page);
        var (employee, vacancy) = (arranged.Employee, arranged.Vacancy);
        using var recruiterApi = arranged.RecruiterApi;

        // Tab 1: open the apply form with a CV chosen, but don't submit yet.
        await page.OpenCardAsync(vacancy.Title);
        await page.ClickApplyAsync();
        await page.SelectValidCvAsync($"cv-{employee.LastName}.pdf", CandidateCvApi.BuildTestPdf());

        // Tab 2 (same browser context, same signed-in employee): apply successfully.
        var otherTab = await _page.Context.NewPageAsync();
        try
        {
            var other = new InternalVacanciesPage(otherTab, _fixture.WebBaseUrl);
            await other.GoToAsync(AcmeId);
            await other.SearchAsync(vacancy.Title);
            await other.OpenCardAsync(vacancy.Title);
            await other.ClickApplyAsync();
            await other.SelectValidCvAsync($"cv2-{employee.LastName}.pdf", CandidateCvApi.BuildTestPdf());
            await other.SubmitApplicationAsync();
            Assert.True(await other.IsSuccessVisibleAsync(), "Expected the second tab's application to succeed.");
        }
        finally
        {
            await otherTab.CloseAsync();
        }

        // Back in tab 1: the stale form's submit is a friendly "already applied", not an error.
        await page.SubmitApplicationAsync();
        Assert.True(await page.IsAlreadyAppliedVisibleAsync(), "Expected the 'already applied' message.");
        Assert.Contains("You have already applied for this vacancy.", await page.GetAlreadyAppliedTextAsync());
        Assert.True(await page.IsAppliedStateVisibleAsync(), "Expected the Applied status in the details.");
        Assert.True(await page.IsAppliedButtonDisabledAsync(), "Expected a disabled Applied footer button.");

        var applications = await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id);
        Assert.Single(applications);
    }

    // ── 5. Work email already used by an external candidate ────────────────────────────────

    [Fact]
    public async Task Apply_WhenWorkEmailBelongsToExternalCandidate_ShowsContactHrMessage_AndNoApplication()
    {
        var page = new InternalVacanciesPage(_page, _fixture.WebBaseUrl);
        var arranged = await ArrangeAsync(page, beforeLogin: async (emp, recruiter) =>
        {
            // A recruiter already holds an external candidate record under the employee's work email.
            await CandidateCvApi.CreateCandidateAsync(recruiter, AcmeId, "External", emp.LastName, emp.WorkEmail);
        });
        var (employee, vacancy) = (arranged.Employee, arranged.Vacancy);
        using var recruiterApi = arranged.RecruiterApi;

        await page.OpenCardAsync(vacancy.Title);
        await page.ClickApplyAsync();
        await page.SelectValidCvAsync($"cv-{employee.LastName}.pdf", CandidateCvApi.BuildTestPdf());
        await page.SubmitApplicationAsync();

        var error = await page.GetServerErrorAsync();
        Assert.Contains("contact HR", error, StringComparison.OrdinalIgnoreCase);
        Assert.True(await page.IsApplyFormVisibleAsync(), "Expected to remain on the apply form after the email-in-use rejection.");
        Assert.Equal(0, await page.AppliedStateCountAsync());

        var applications = await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id);
        Assert.Empty(applications);
    }
}
