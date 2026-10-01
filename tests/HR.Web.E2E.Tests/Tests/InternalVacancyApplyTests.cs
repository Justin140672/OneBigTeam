using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class InternalVacancyApplyTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = InternalVacancyApplyApi.AcmeId;

    private const string PdfMime = "application/pdf";
    private const long MaxCvBytes = 20 * 1024 * 1024;

    private sealed record Arranged(
        InternalVacancyApplyApi.FreshEmployee Employee,
        InternalVacancyApplyApi.FreshVacancy Vacancy,
        HttpClient RecruiterApi);

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

        await page.SubmitApplicationAsync();
        await page.WaitForCvErrorAsync("Please choose a CV file to upload.");
        Assert.True(await page.IsApplyFormVisibleAsync(), "Expected to remain on the apply form after a missing-CV submit.");

        await page.SelectValidCvAsync($"cv-{employee.LastName}.pdf", CandidateCvApi.BuildTestPdf());
        await page.CancelApplyAsync();

        Assert.True(await page.IsDetailVisibleAsync(), "Expected the vacancy details after Cancel.");
        Assert.True(await page.IsApplyButtonVisibleAsync(), "Expected the Apply button to be back after Cancel.");
        Assert.True(await page.WaitForFocusAsync("internal-vacancy-apply"),
            $"Expected focus to return to the Apply button after Cancel, but it was on '{await page.ActiveElementTestIdAsync()}'.");

        var applications = await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id);
        Assert.Empty(applications);
    }


    [Fact]
    public async Task ApplyForm_RejectsWrongTypeOversizedAndEmptyFiles()
    {
        var page = new InternalVacanciesPage(_page, _fixture.WebBaseUrl);
        var arranged = await ArrangeAsync(page);
        var vacancy = arranged.Vacancy;
        using var recruiterApi = arranged.RecruiterApi;

        await page.OpenCardAsync(vacancy.Title);
        await page.ClickApplyAsync();

        await page.SelectCvAsync("cv.txt", "text/plain", "plain text CV"u8.ToArray());
        await page.WaitForCvErrorAsync("The CV must be a PDF file.");
        Assert.False(await page.IsCvSelectedVisibleAsync(), "A rejected .txt file must not be shown as selected.");

        await page.SelectCvAsync("empty.pdf", PdfMime, []);
        await page.WaitForCvErrorAsync("The selected CV file is empty.");
        Assert.False(await page.IsCvSelectedVisibleAsync(), "A rejected empty file must not be shown as selected.");

        var oversized = new byte[MaxCvBytes + 1];
        CandidateCvApi.BuildTestPdf().AsSpan(0, 5).CopyTo(oversized);
        await page.SelectCvAsync("huge.pdf", PdfMime, oversized);
        await page.WaitForCvErrorAsync("The CV file is larger than the 20 MB limit.");
        Assert.False(await page.IsCvSelectedVisibleAsync(), "A rejected oversized file must not be shown as selected.");

        Assert.True(await page.IsApplyFormVisibleAsync(), "Expected to remain on the apply form after rejected files.");

        var applications = await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id);
        Assert.Empty(applications);
    }


    [Fact]
    public async Task Apply_WithPdfCv_ShowsSubmittedAndAppliedState_AndPersistsAfterReload()
    {
        var page = new InternalVacanciesPage(_page, _fixture.WebBaseUrl);
        var arranged = await ArrangeAsync(page);
        var (employee, vacancy) = (arranged.Employee, arranged.Vacancy);
        using var recruiterApi = arranged.RecruiterApi;

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


    [Fact]
    public async Task Apply_WhenAlreadyAppliedInAnotherTab_ShowsFriendlyAlreadyAppliedMessage_AndAppliedState()
    {
        var page = new InternalVacanciesPage(_page, _fixture.WebBaseUrl);
        var arranged = await ArrangeAsync(page);
        var (employee, vacancy) = (arranged.Employee, arranged.Vacancy);
        using var recruiterApi = arranged.RecruiterApi;

        await page.OpenCardAsync(vacancy.Title);
        await page.ClickApplyAsync();
        await page.SelectValidCvAsync($"cv-{employee.LastName}.pdf", CandidateCvApi.BuildTestPdf());

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

        await page.SubmitApplicationAsync();
        Assert.True(await page.IsAlreadyAppliedVisibleAsync(), "Expected the 'already applied' message.");
        Assert.Contains("You have already applied for this vacancy.", await page.GetAlreadyAppliedTextAsync());
        Assert.True(await page.IsAppliedStateVisibleAsync(), "Expected the Applied status in the details.");
        Assert.True(await page.IsAppliedButtonDisabledAsync(), "Expected a disabled Applied footer button.");

        var applications = await CandidateCvApi.ListApplicationsForVacancyAsync(recruiterApi, AcmeId, vacancy.Id);
        Assert.Single(applications);
    }


    [Fact]
    public async Task Apply_WhenWorkEmailBelongsToExternalCandidate_ShowsContactHrMessage_AndNoApplication()
    {
        var page = new InternalVacanciesPage(_page, _fixture.WebBaseUrl);
        var arranged = await ArrangeAsync(page, beforeLogin: async (emp, recruiter) =>
        {
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
