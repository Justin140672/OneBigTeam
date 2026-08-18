using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// End-to-end smoke test covering the full recruitment pipeline: a candidate applies to a
/// vacancy, is interviewed, offered the role, and hired — at which point the Recruitment
/// module provisions a real Employee record and links it back to the candidate.
///
/// Candidate → Application → Interview (scheduled + outcome recorded) → Offer → Hire → Employee
///
/// Uses the seeded Acme company (00000000-0000-0000-0000-000000000001) and James Okafor
/// (30000000-0000-0000-0000-000000000002) as the hiring manager / interviewer, but creates a
/// fresh Vacancy and Candidate each run (unique names) so the test can be re-run against the
/// same database without colliding with a previous run's data.
///
/// Runs as Marcus Diallo (Recruiter role) throughout the recruitment steps — recruitment:manage/
/// candidate:view are Recruiter-only (see IdentityModule.AddRolePolicies), and an HR Administrator
/// does not automatically get recruitment access. The final "employee now exists" check is a
/// genuinely separate permission (employee:manage, HR-Administrator-only) that a Recruiter does
/// not hold, so that one step switches to Laura Bennett (HR Administrator) — mirroring the real
/// handoff: a Recruiter hires someone, an HR Administrator manages their employee record after.
/// </summary>
public sealed class ApplicationToEmployeeFlowTests(CrossUserFixture fixture) : CrossUserVacancyTestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Candidate_Applies_Interviews_IsOffered_AndHired_BecomesEmployee()
    {
        var unique         = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast  = $"Cand{unique}";
        var candidateEmail = $"e2e.cand{unique}@example.com";
        var vacancyTitle   = $"E2E Test Role {unique}";

        var login          = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList  = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit  = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList    = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail  = new VacancyDetailPage(_page, _fixture.WebBaseUrl);
        var employeeList   = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // ── Step 1: Create the candidate ──────────────────────────────────────────
        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickNewCandidateAsync();
        await candidateEdit.FillFirstNameAsync(candidateFirst);
        await candidateEdit.FillLastNameAsync(candidateLast);
        await candidateEdit.FillEmailAsync(candidateEmail);
        await candidateEdit.SaveNewCandidateAsync();

        Assert.True(await candidateList.HasCandidateAsync(candidateLast),
            $"Expected the new candidate '{candidateLast}' to appear in the candidate list");

        // ── Step 2: Create the vacancy ─────────────────────────────────────────────
        // Position Profile is mandatory for creation (the API rejects a vacancy with no
        // PositionProfileId belonging to the same company) — "Senior Software Engineer" is
        // seeded for Acme (see EmployeesModule.SeedEmployeesAsync).
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        Assert.True(await vacancyList.HasVacancyAsync(vacancyTitle),
            $"Expected the new vacancy '{vacancyTitle}' to appear in the vacancy list");

        // ── Step 3: Add the candidate's application to the vacancy ────────────────
        await vacancyList.ClickVacancyAsync(vacancyTitle);
        await vacancyDetail.PublishVacancyAsync();
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickAddCandidateAsync();
        // The Add Candidate popup's item text is name-only (no email — item #26/product fix),
        // so the candidate must be matched by name here even though search-as-you-type still
        // works server-side against any field including email.
        await vacancyDetail.SelectCandidateInAddDialogAsync(candidateLast);
        await vacancyDetail.SubmitAddApplicationAsync();

        Assert.Equal("Application Received", await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        // ── Step 4: Schedule an interview ──────────────────────────────────────────
        await vacancyDetail.ClickScheduleInterviewForAsync(candidateLast);
        await vacancyDetail.WaitForScheduleDialogAsync();
        await vacancyDetail.SelectInterviewerAsync("James");
        await vacancyDetail.FillScheduledAtAsync("01/09/2026 10:00");
        await vacancyDetail.SubmitScheduleInterviewAsync();

        // Scheduling an interview never moves CurrentStageId — it's pure metadata (an Interview
        // row plus InterviewOutcome defaulting to Pending); stage-shaped interview sub-states no
        // longer exist (see ScheduleInterviewHandler's "Ticket #99" comment), moving stage is a
        // separate explicit action, so the status badge is unchanged from "Application Received".
        Assert.Equal("Application Received", await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        // ── Step 5: Record the interview outcome ───────────────────────────────────
        await vacancyDetail.OpenInterviewsTabAsync();
        Assert.Equal("Pending", await vacancyDetail.GetInterviewOutcomeAsync(candidateLast));

        await vacancyDetail.ClickRecordOutcomeForAsync(candidateLast);
        await vacancyDetail.WaitForOutcomeDialogAsync();
        await vacancyDetail.SelectOutcomeAsync("Passed");
        await vacancyDetail.SubmitOutcomeAsync();

        Assert.Equal("Passed", await vacancyDetail.GetInterviewOutcomeAsync(candidateLast));

        // ── Step 6: Make an offer ───────────────────────────────────────────────────
        // Recording the interview outcome, like scheduling it, is pure metadata (Application.
        // InterviewOutcome) and never itself moves CurrentStageId either — see the same "Ticket #99"
        // rationale referenced above. Status stays "Application Received" until an explicit
        // stage-moving action (OfferCandidate below) runs.
        await vacancyDetail.OpenApplicationsTabAsync();
        Assert.Equal("Application Received", await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        await vacancyDetail.ClickOfferForAsync(candidateLast);
        Assert.Equal("Offer", await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        // ── Step 7: Hire — this provisions a real Employee and links the candidate ─
        await vacancyDetail.ClickHireForAsync(candidateLast);
        await vacancyDetail.WaitForHireDialogAsync();
        await vacancyDetail.FillHireStartDateAsync("01/10/2026");
        await vacancyDetail.FillHireDateOfBirthAsync("15/06/1990");
        await vacancyDetail.SelectHireNationalityAsync("British");
        await vacancyDetail.SelectHireGenderAsync("Male");

        // Employee Number and Employment Type are still filled in manually — Department,
        // Location and Position Profile are no longer manual fields as of the "Vacancy - Position
        // Profile relationship" epic: they're derived server-side by HireCandidateHandler from the
        // Vacancy's own linked Position Profile ("Senior Software Engineer", selected when the
        // vacancy was created above) and shown read-only in the dialog for confirmation.
        Assert.Equal("Senior Software Engineer", await vacancyDetail.GetHireDerivedPositionProfileTextAsync());
        Assert.Equal("London Office", await vacancyDetail.GetHireDerivedLocationTextAsync());

        await vacancyDetail.FillHireEmployeeNumberAsync($"E2E-{unique}");
        await vacancyDetail.SelectHireDropdownAsync("Employment Type", "Permanent");

        await vacancyDetail.SubmitHireAsync();

        Assert.Equal("Hired", await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        // ── Step 8: The candidate record should now show it was hired and linked ──
        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickCandidateAsync(candidateLast);
        Assert.True(await candidateEdit.HasHiredBannerAsync(),
            "Expected the candidate detail page to show the 'hired and linked to employee' banner");

        // ── Step 9: A real Employee should now exist and appear in the employee list ─
        // The employee list page requires employee:manage, which Marcus (Recruiter) doesn't
        // hold — switch to Laura (HR Administrator) for this one check, matching the real
        // handoff between the two roles.
        await login.SwitchAccountAsync(LauraEmail);
        await employeeList.GoToAsync(AcmeId);
        Assert.True(await employeeList.HasEmployeeAsync(candidateLast),
            $"Expected an employee named '{candidateLast}' to appear in the employee list after hiring");
    }

    [Fact]
    public async Task HireCandidateDialog_MissingNewlyRequiredFields_ShowsValidationError_AndDoesNotHire()
    {
        var (candidateLast, vacancyDetail) = await ArrangeOfferedApplicationAsync();

        await vacancyDetail.ClickHireForAsync(candidateLast);
        await vacancyDetail.WaitForHireDialogAsync();

        // The manual Department/Location/Position Profile dropdowns are gone entirely — those
        // values are now shown read-only, derived from the Vacancy's linked Position Profile.
        Assert.False(await vacancyDetail.HasHireDropdownLabelAsync("Department"),
            "Expected the manual Department dropdown to no longer exist in the Hire dialog");
        Assert.False(await vacancyDetail.HasHireDropdownLabelAsync("Location"),
            "Expected the manual Location dropdown to no longer exist in the Hire dialog");
        Assert.False(await vacancyDetail.HasHireDropdownLabelAsync("Position Profile"),
            "Expected the manual Position Profile dropdown to no longer exist in the Hire dialog");
        Assert.Equal("Senior Software Engineer", await vacancyDetail.GetHireDerivedPositionProfileTextAsync());
        Assert.Equal("London Office", await vacancyDetail.GetHireDerivedLocationTextAsync());

        // Fill in the fields that were already required before Employment Type/Employee Number
        // became mandatory...
        await vacancyDetail.FillHireStartDateAsync("01/10/2026");
        await vacancyDetail.FillHireDateOfBirthAsync("15/06/1990");
        await vacancyDetail.SelectHireNationalityAsync("British");
        await vacancyDetail.SelectHireGenderAsync("Male");

        // ...but deliberately leave the newly-required manual fields (Employee Number, Employment
        // Type) blank and attempt to submit anyway. Department/Location/Position Profile are no
        // longer manual inputs at all as of the "Vacancy - Position Profile relationship" epic —
        // they're derived server-side from the Vacancy's linked Position Profile, so they can't be
        // "left blank" here the way they used to be.
        await vacancyDetail.ClickHireSubmitButtonAsync();

        await _page.WaitForSelectorAsync(".hire-candidate-dialog .alert-danger", new() { Timeout = 10_000 });
        Assert.True(await vacancyDetail.HasDialogErrorAsync("hire-candidate-dialog"),
            "Expected a validation error when submitting the Hire dialog without the newly required fields");

        // The dialog stayed open rather than closing (which SubmitHireAsync would otherwise wait
        // for), confirming the hire did not go through — the application should still be Offered.
        await vacancyDetail.CancelHireDialogAsync();
        Assert.Equal("Offer", await vacancyDetail.GetApplicationStatusAsync(candidateLast));
    }

    /// <summary>
    /// Runs the candidate/vacancy/application/interview/offer pipeline (steps 1-6 of
    /// <see cref="Candidate_Applies_Interviews_IsOffered_AndHired_BecomesEmployee"/>) so a test can
    /// exercise just the Hire dialog against a fresh Offered application, without duplicating this
    /// multi-step setup inline.
    /// </summary>
    private async Task<(string CandidateLast, VacancyDetailPage VacancyDetail)> ArrangeOfferedApplicationAsync()
    {
        var unique         = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast  = $"Cand{unique}";
        var candidateEmail = $"e2e.cand{unique}@example.com";
        var vacancyTitle   = $"E2E Test Role {unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickNewCandidateAsync();
        await candidateEdit.FillFirstNameAsync(candidateFirst);
        await candidateEdit.FillLastNameAsync(candidateLast);
        await candidateEdit.FillEmailAsync(candidateEmail);
        await candidateEdit.SaveNewCandidateAsync();

        // Position Profile is mandatory for creation — "Senior Software Engineer" is seeded for Acme.
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.ClickVacancyAsync(vacancyTitle);
        await vacancyDetail.PublishVacancyAsync();
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickAddCandidateAsync();
        // The Add Candidate popup's item text is name-only (no email — item #26/product fix),
        // so the candidate must be matched by name here even though search-as-you-type still
        // works server-side against any field including email.
        await vacancyDetail.SelectCandidateInAddDialogAsync(candidateLast);
        await vacancyDetail.SubmitAddApplicationAsync();

        await vacancyDetail.ClickScheduleInterviewForAsync(candidateLast);
        await vacancyDetail.WaitForScheduleDialogAsync();
        await vacancyDetail.SelectInterviewerAsync("James");
        await vacancyDetail.FillScheduledAtAsync("01/09/2026 10:00");
        await vacancyDetail.SubmitScheduleInterviewAsync();

        await vacancyDetail.OpenInterviewsTabAsync();
        await vacancyDetail.ClickRecordOutcomeForAsync(candidateLast);
        await vacancyDetail.WaitForOutcomeDialogAsync();
        await vacancyDetail.SelectOutcomeAsync("Passed");
        await vacancyDetail.SubmitOutcomeAsync();

        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickOfferForAsync(candidateLast);

        return (candidateLast, vacancyDetail);
    }
}
