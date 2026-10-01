using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Accessibility of the recruitment screens (dashboard, vacancy / candidate / external recruiter
/// forms, lists and reports): programmatic names and labels, required / invalid state and error
/// association, input types and autocomplete tokens, CV upload semantics, search-result
/// announcements, DOM-order keyboard focus, and no control named with a generic widget type.
/// Recruitment stages live in RecruitmentStageAccessibilityTests (shared-list collection).
///
/// Every test seeds its own vacancy / candidate / recruiter with unique values through the API.
/// </summary>
public sealed class RecruitmentAccessibilityTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = RecruitmentSeedApi.AcmeId;

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    private async Task LoginAsync(string email = MarcusEmail)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(email);
    }

    private string Url(string path) => $"{_fixture.WebBaseUrl}/companies/{AcmeId}/{path}";

    private async Task WaitForGridAsync() =>
        await _page.Locator(".e-grid .e-row, .e-grid .e-emptyrow").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

    private ILocator Announcer => _page.GetByTestId("search-results-announcer").First;

    private async Task OpenNewVacancyFormAsync()
    {
        await LoginAsync();
        await new VacancyDetailPage(_page, _fixture.WebBaseUrl).GoToNewAsync(AcmeId);
    }

    private async Task<(Guid VacancyId, string Title)> OpenSeededVacancyAsync(string route = "")
    {
        using var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl);
        var vacancy = await seed.CreateOpenVacancyAsync();
        await LoginAsync();
        await _page.GotoAsync(Url($"vacancies/{vacancy.Id}{route}"));
        await Assertions.Expect(_page.GetByRole(AriaRole.Tab, new() { Name = "Overview" }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        return (vacancy.Id, vacancy.Title);
    }

    private async Task<AddCandidateDialog> OpenAddCandidateDialogAsync()
    {
        await OpenSeededVacancyAsync();
        var detail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);
        await detail.OpenApplicationsTabAsync();
        await detail.ClickAddCandidateAsync();
        var dialog = new AddCandidateDialog(_page);
        await dialog.ExpectOpenAsync();
        return dialog;
    }

    private async Task<Guid> OpenSeededCandidateAsync(string route = "")
    {
        using var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl);
        var candidate = await seed.CreateCandidateAsync();
        await LoginAsync();
        await _page.GotoAsync(Url($"candidates/{candidate.Id}{route}"));
        await Assertions.Expect(_page.Locator("#candidate-first-name")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        return candidate.Id;
    }

    private async Task<Guid> OpenSeededRecruiterAsync(string route = "")
    {
        using var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl);
        var id = await seed.CreateExternalRecruiterAsync($"E2E A11y Agency {Guid.NewGuid().ToString("N")[..8]}");
        await LoginAsync();
        await _page.GotoAsync(Url($"external-recruiters/{id}{route}"));
        await Assertions.Expect(_page.Locator("#recruiter-agency-name")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        return id;
    }

    private async Task OpenKeyPageAsync(string key)
    {
        switch (key)
        {
            case "dashboard":
                await LoginAsync();
                await new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl).GoToAsync();
                await Assertions.Expect(_page.Locator("[data-testid='recruitment-view-board-btn']"))
                    .ToBeVisibleAsync(new() { Timeout = 30_000 });
                break;
            case "vacancy-list":
                await LoginAsync();
                await _page.GotoAsync(Url("vacancies"));
                await WaitForGridAsync();
                break;
            case "vacancy-new":
                await OpenNewVacancyFormAsync();
                break;
            case "vacancy-edit":
                await OpenSeededVacancyAsync();
                break;
            case "vacancy-view":
                await OpenSeededVacancyAsync("/view");
                break;
            case "candidate-list":
                await LoginAsync();
                await _page.GotoAsync(Url("candidates"));
                await WaitForGridAsync();
                break;
            case "candidate-new":
                await LoginAsync();
                await new CandidateEditPage(_page, _fixture.WebBaseUrl).GoToNewAsync(AcmeId);
                break;
            case "candidate-edit":
                await OpenSeededCandidateAsync();
                break;
            case "recruiter-list":
                await LoginAsync();
                await _page.GotoAsync(Url("external-recruiters"));
                await WaitForGridAsync();
                break;
            case "recruiter-new":
                await LoginAsync();
                await new ExternalRecruiterDetailPage(_page, _fixture.WebBaseUrl).GoToNewAsync(AcmeId);
                break;
            case "recruiter-edit":
                await OpenSeededRecruiterAsync();
                break;
            case "report-pipeline":
            case "report-summary":
            case "report-performance":
                await LoginAsync();
                var slug = key switch
                {
                    "report-pipeline" => "recruitment-pipeline",
                    "report-summary" => "recruitment-pipeline-summary",
                    _ => "vacancy-performance",
                };
                await _page.GotoAsync(Url($"reporting/{slug}"));
                await WaitForGridAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, null);
        }
    }

    // ── Dashboard ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dashboard_TabsToolbarAndSummaryTiles_HaveAccessibleNames()
    {
        await OpenKeyPageAsync("dashboard");

        await Assertions.Expect(_page.GetByRole(AriaRole.Tablist, new() { Name = "Recruitment dashboard sections" })).ToBeVisibleAsync();
        foreach (var tab in new[] { "Pipeline", "Activity", "Insights" })
            await Assertions.Expect(_page.GetByRole(AriaRole.Tab, new() { Name = tab, Exact = true })).ToBeVisibleAsync();

        await Assertions.Expect(_page.GetByRole(AriaRole.Combobox, new() { Name = "Selected vacancy" }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(_page.GetByRole(AriaRole.Textbox, new() { Name = "Search pipeline candidates", Exact = true }))
            .ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByLabel("Show closed candidates")).ToBeVisibleAsync();

        await new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl).WaitForSummaryTilesLoadedAsync();
        foreach (var label in new[]
                 {
                     "Open vacancies", "New applications", "Candidates in progress",
                     "Interviews requiring action", "Offers awaiting response", "Stale vacancies",
                 })
        {
            await Assertions.Expect(_page.GetByLabel(new Regex($"^{Regex.Escape(label)}: \\d+$")))
                .ToHaveCountAsync(1, new() { Timeout = 20_000 });
        }
    }

    [Fact]
    public async Task Dashboard_ViewToggle_IsAnExclusivePressedGroup_AndAnnouncesTheSwitch()
    {
        await OpenKeyPageAsync("dashboard");

        var group = _page.GetByRole(AriaRole.Group, new() { Name = "Pipeline view" });
        var board = group.GetByRole(AriaRole.Button, new() { Name = "Board", Exact = true });
        var list = group.GetByRole(AriaRole.Button, new() { Name = "List", Exact = true });
        var announcer = _page.GetByTestId("recruitment-view-announcer");

        await Assertions.Expect(board).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(list).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(announcer).ToHaveAttributeAsync("role", "status");
        await Assertions.Expect(announcer).ToHaveAttributeAsync("aria-live", "polite");

        await list.ClickAsync();
        await Assertions.Expect(list).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(board).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(announcer).ToHaveTextAsync("List view selected");

        await board.ClickAsync();
        await Assertions.Expect(board).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(list).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(announcer).ToHaveTextAsync("Board view selected");
    }

    [Fact]
    public async Task Dashboard_TabKey_ReachesToolbarControlsInDomOrder()
    {
        await OpenKeyPageAsync("dashboard");
        await Assertions.Expect(_page.GetByRole(AriaRole.Textbox, new() { Name = "Search pipeline candidates", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        await A11yAssert.TabReachesInOrderAsync(
            _page,
            _page.Locator("#recruitment-tab-pipeline"),
            ["recruitment-board-vacancy-picker", "Search pipeline candidates", "show-terminal-stages",
             "recruitment-view-board-btn", "recruitment-view-list-btn"]);
    }

    // ── Vacancy form ────────────────────────────────────────────────────────────

    [Fact]
    public async Task VacancyForm_Create_FieldsAreLabelled_AndRequiredControlsAreMarked()
    {
        await OpenNewVacancyFormAsync();

        await A11yAssert.LabelledAsync(_page, "vacancy-position-profile", "Position Profile");
        await A11yAssert.LabelledAsync(_page, "vacancy-recruitment-agency", "Recruitment Agency");
        await Assertions.Expect(_page.GetByLabel("Advert Title", new() { Exact = true }))
            .ToHaveAttributeAsync("id", "vacancy-advert-title");
        await Assertions.Expect(_page.GetByLabel("Advert Description", new() { Exact = true }))
            .ToHaveAttributeAsync("id", "vacancy-advert-description");
        await Assertions.Expect(_page.Locator("#vacancy-hiring-manager"))
            .ToHaveAttributeAsync("aria-label", "Hiring Manager");
        await Assertions.Expect(_page.Locator("label[for='vacancy-hiring-manager']")).ToContainTextAsync("Hiring Manager");
        await Assertions.Expect(_page.GetByLabel("Advertise this vacancy to employees")).ToHaveCountAsync(1);

        await A11yAssert.RequiredAsync(_page.Locator("#vacancy-position-profile"));
        await A11yAssert.RequiredAsync(_page.Locator("#vacancy-hiring-manager"));
        await Assertions.Expect(_page.Locator("#vacancy-advert-title")).Not.ToHaveAttributeAsync("aria-required", "true");
        await Assertions.Expect(_page.Locator("#vacancy-recruitment-agency")).Not.ToHaveAttributeAsync("aria-required", "true");
    }

    [Fact]
    public async Task VacancyForm_EditMode_FieldsAreLabelled()
    {
        await OpenSeededVacancyAsync();

        await A11yAssert.LabelledAsync(_page, "vacancy-position-profile", "Position Profile");
        await A11yAssert.LabelledAsync(_page, "vacancy-recruitment-agency", "Recruitment Agency");
        await Assertions.Expect(_page.GetByLabel("Advert Title", new() { Exact = true }))
            .ToHaveAttributeAsync("id", "vacancy-advert-title");
        await Assertions.Expect(_page.Locator("#vacancy-hiring-manager"))
            .ToHaveAttributeAsync("aria-label", "Hiring Manager");
    }

    [Fact]
    public async Task VacancyForm_SubmitEmpty_MarksInvalidFields_AssociatesMessages_AndFocusesFirstInvalid()
    {
        await OpenNewVacancyFormAsync();

        await _page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await Assertions.Expect(_page.Locator("div.alert-danger[role='alert']"))
            .ToContainTextAsync("Please correct the highlighted fields", new() { Timeout = 15_000 });
        await A11yAssert.InvalidWithAssociatedMessageAsync(_page, _page.Locator("#vacancy-position-profile"));
        await A11yAssert.InvalidWithAssociatedMessageAsync(_page, _page.Locator("#vacancy-hiring-manager"));
        await A11yAssert.FocusIsInsideFieldAsync(_page, "Position Profile");
    }

    [Fact]
    public async Task VacancyForm_ViewMode_ControlsAreDisabledOrReadOnly()
    {
        await OpenSeededVacancyAsync("/view");

        await Assertions.Expect(_page.Locator("#vacancy-advert-title")).Not.ToBeEditableAsync();
        await Assertions.Expect(_page.Locator("#vacancy-advert-description")).Not.ToBeEditableAsync();
        await A11yAssert.IsDisabledOrReadOnlyAsync(_page.Locator("#vacancy-position-profile"));
        await A11yAssert.IsDisabledOrReadOnlyAsync(_page.Locator("#vacancy-hiring-manager"));
        await A11yAssert.IsDisabledOrReadOnlyAsync(_page.Locator("#vacancy-recruitment-agency"));
        await Assertions.Expect(_page.Locator("#isAdvertisedInternally")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task VacancyForm_TabKey_ReachesFieldsAndFooterButtonsInDomOrder()
    {
        await OpenNewVacancyFormAsync();

        await A11yAssert.TabReachesInOrderAsync(
            _page,
            _page.GetByLabel("Advert Title", new() { Exact = true }),
            ["vacancy-hiring-manager", "vacancy-recruitment-agency", "vacancy-advert-description",
             "isAdvertisedInternally", "Save", "Cancel"]);
    }

    // ── Vacancy applications tab: Add Candidate dialog ──────────────────────────

    [Fact]
    public async Task AddCandidateDialog_ExistingMode_FieldsAreNamed()
    {
        var dialog = await OpenAddCandidateDialogAsync();
        var root = dialog.Dialog;

        await Assertions.Expect(root.GetByRole(AriaRole.Group, new() { Name = "Candidate type" })).ToBeVisibleAsync();
        await A11yAssert.LabelledAsync(_page, "add-application-candidate", "Candidate");
        await A11yAssert.RequiredAsync(_page.Locator("#add-application-candidate"));
        await A11yAssert.LabelledAsync(_page, "add-application-source", "Source");
        await Assertions.Expect(root.GetByLabel("Notes (optional)")).ToHaveAttributeAsync("id", "add-application-notes");
    }

    [Fact]
    public async Task AddCandidateDialog_NewCandidateFields_HaveInputTypesAndAutocompleteTokens()
    {
        var dialog = await OpenAddCandidateDialogAsync();
        await dialog.SwitchToNewModeAsync();
        var root = dialog.Dialog;

        await Assertions.Expect(root.GetByLabel("First name")).ToHaveAttributeAsync("id", "new-candidate-first-name");
        await Assertions.Expect(root.GetByLabel("Last name")).ToHaveAttributeAsync("id", "new-candidate-last-name");
        await Assertions.Expect(root.GetByLabel("Email")).ToHaveAttributeAsync("id", "new-candidate-email");
        await Assertions.Expect(root.GetByLabel("Phone (optional)")).ToHaveAttributeAsync("id", "new-candidate-phone");

        await A11yAssert.InputAttributesAsync(root.Locator("#new-candidate-first-name"), "text", "given-name");
        await A11yAssert.InputAttributesAsync(root.Locator("#new-candidate-last-name"), "text", "family-name");
        await A11yAssert.InputAttributesAsync(root.Locator("#new-candidate-email"), "email", "email");
        await A11yAssert.InputAttributesAsync(root.Locator("#new-candidate-phone"), "tel", "tel");
        await A11yAssert.NoAutocompleteOnAsync(root);
    }

    [Fact]
    public async Task AddCandidateDialog_NewCandidate_EmptySubmit_AssociatesErrorsWithFields()
    {
        var dialog = await OpenAddCandidateDialogAsync();
        await dialog.SwitchToNewModeAsync();

        await dialog.ClickSubmitAsync();

        foreach (var id in new[] { "new-candidate-first-name", "new-candidate-last-name", "new-candidate-email" })
            await A11yAssert.InvalidWithAssociatedMessageAsync(_page, dialog.Dialog.Locator($"#{id}"));
    }

    [Fact]
    public async Task AddCandidateDialog_CvInput_HasNameHelpText_SelectionAnnouncement_AndRejectionAlert()
    {
        var dialog = await OpenAddCandidateDialogAsync();
        await dialog.SwitchToNewModeAsync();
        var root = dialog.Dialog;
        var input = root.Locator("#new-candidate-cv-input");

        await Assertions.Expect(root.GetByLabel("CV file (PDF)")).ToHaveAttributeAsync("id", "new-candidate-cv-input");
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-describedby", new Regex("new-candidate-cv-help"));
        var help = root.Locator("#new-candidate-cv-help");
        await Assertions.Expect(help).ToContainTextAsync("PDF");
        await Assertions.Expect(help).ToContainTextAsync("MB");

        var fileName = $"e2e-a11y-cv-{Guid.NewGuid():N}.pdf";
        await dialog.SetCvFileAsync(fileName, CandidateCvApi.BuildTestPdf(), "application/pdf");
        await Assertions.Expect(root.Locator("[role='status']").Filter(new() { HasText = $"Selected CV: {fileName}" }))
            .ToHaveCountAsync(1, new() { Timeout = 15_000 });

        await dialog.SetCvFileAsync($"e2e-not-a-cv-{Guid.NewGuid():N}.txt", "plain text"u8.ToArray(), "text/plain");
        var error = root.Locator("#new-candidate-cv-error");
        await Assertions.Expect(error).ToHaveAttributeAsync("role", "alert", new() { Timeout = 15_000 });
        await Assertions.Expect(error).ToContainTextAsync("PDF file");
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-describedby", new Regex("new-candidate-cv-error"));
    }

    // ── Candidate form ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CandidateForm_Create_FieldsAreLabelled_WithTypesAndAutocompleteTokens()
    {
        await OpenKeyPageAsync("candidate-new");

        await Assertions.Expect(_page.GetByLabel("First name")).ToHaveAttributeAsync("id", "new-candidate-first-name");
        await Assertions.Expect(_page.GetByLabel("Last name")).ToHaveAttributeAsync("id", "new-candidate-last-name");
        await Assertions.Expect(_page.GetByLabel("Email")).ToHaveAttributeAsync("id", "new-candidate-email");
        await Assertions.Expect(_page.GetByLabel("Phone (optional)")).ToHaveAttributeAsync("id", "new-candidate-phone");

        await A11yAssert.InputAttributesAsync(_page.Locator("#new-candidate-first-name"), "text", "given-name");
        await A11yAssert.InputAttributesAsync(_page.Locator("#new-candidate-last-name"), "text", "family-name");
        await A11yAssert.InputAttributesAsync(_page.Locator("#new-candidate-email"), "email", "email");
        await A11yAssert.InputAttributesAsync(_page.Locator("#new-candidate-phone"), "tel", "tel");
        await A11yAssert.NoAutocompleteOnAsync(_page.Locator("form"));

        await A11yAssert.RequiredAsync(_page.Locator("#new-candidate-first-name"));
        await A11yAssert.RequiredAsync(_page.Locator("#new-candidate-last-name"));
        await A11yAssert.RequiredAsync(_page.Locator("#new-candidate-email"));
    }

    [Fact]
    public async Task CandidateForm_Edit_FieldsAreLabelled_WithTypesAndAutocompleteTokens()
    {
        await OpenSeededCandidateAsync();

        await Assertions.Expect(_page.GetByLabel("First Name")).ToHaveAttributeAsync("id", "candidate-first-name");
        await Assertions.Expect(_page.GetByLabel("Last Name")).ToHaveAttributeAsync("id", "candidate-last-name");
        await Assertions.Expect(_page.GetByLabel("Email")).ToHaveAttributeAsync("id", "candidate-email");
        await Assertions.Expect(_page.GetByLabel("Phone", new() { Exact = true })).ToHaveAttributeAsync("id", "candidate-phone");

        await A11yAssert.InputAttributesAsync(_page.Locator("#candidate-first-name"), "text", "given-name");
        await A11yAssert.InputAttributesAsync(_page.Locator("#candidate-last-name"), "text", "family-name");
        await A11yAssert.InputAttributesAsync(_page.Locator("#candidate-email"), "email", "email");
        await A11yAssert.InputAttributesAsync(_page.Locator("#candidate-phone"), "tel", "tel");
        await A11yAssert.NoAutocompleteOnAsync(_page.Locator("form"));
    }

    [Fact]
    public async Task CandidateForm_Create_EmptySubmit_AssociatesErrors_AnnouncesAlert_AndFocusesFirstInvalid()
    {
        await OpenKeyPageAsync("candidate-new");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();

        await Assertions.Expect(_page.Locator("div.alert-danger[role='alert']"))
            .ToContainTextAsync("Please correct the highlighted fields", new() { Timeout = 15_000 });
        foreach (var id in new[] { "new-candidate-first-name", "new-candidate-last-name", "new-candidate-email" })
            await A11yAssert.InvalidWithAssociatedMessageAsync(_page, _page.Locator($"#{id}"));
        await A11yAssert.FocusIsInsideFieldAsync(_page, "First name");
    }

    [Fact]
    public async Task CandidateForm_Edit_ClearedRequiredField_IsInvalidWithAssociatedMessage()
    {
        await OpenSeededCandidateAsync();

        await _page.Locator("#candidate-first-name").FillAsync("");
        await _page.Keyboard.PressAsync("Tab");
        await _page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await A11yAssert.InvalidWithAssociatedMessageAsync(_page, _page.Locator("#candidate-first-name"));
        await A11yAssert.FocusIsInsideFieldAsync(_page, "First Name");
    }

    [Fact]
    public async Task CandidateCvUpload_NewCandidatePage_HasNameHelp_SelectionAnnouncement_AndErrors()
    {
        await OpenKeyPageAsync("candidate-new");
        var input = _page.Locator("#new-candidate-cv-input");

        await Assertions.Expect(_page.GetByLabel("CV file (PDF)")).ToHaveAttributeAsync("id", "new-candidate-cv-input");
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-describedby", new Regex("new-candidate-cv-help"));
        await Assertions.Expect(_page.Locator("#new-candidate-cv-help")).ToContainTextAsync("PDF");
        await Assertions.Expect(_page.Locator("#new-candidate-cv-help")).ToContainTextAsync("MB");

        var fileName = $"e2e-a11y-cv-{Guid.NewGuid():N}.pdf";
        await input.SetInputFilesAsync(new FilePayload
        {
            Name = fileName,
            MimeType = "application/pdf",
            Buffer = CandidateCvApi.BuildTestPdf(),
        });
        await Assertions.Expect(_page.Locator("[role='status']").Filter(new() { HasText = $"Selected CV: {fileName}" }))
            .ToHaveCountAsync(1, new() { Timeout = 15_000 });

        await input.SetInputFilesAsync(new FilePayload
        {
            Name = $"e2e-not-a-cv-{Guid.NewGuid():N}.txt",
            MimeType = "text/plain",
            Buffer = "plain text"u8.ToArray(),
        });
        var error = _page.Locator("#new-candidate-cv-error");
        await Assertions.Expect(error).ToHaveAttributeAsync("role", "alert", new() { Timeout = 15_000 });
        await Assertions.Expect(error).ToContainTextAsync("PDF file");
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-invalid", "true");

        await input.SetInputFilesAsync(new FilePayload
        {
            Name = $"e2e-too-big-{Guid.NewGuid():N}.pdf",
            MimeType = "application/pdf",
            Buffer = new byte[(20 * 1024 * 1024) + 1],
        });
        await Assertions.Expect(error).ToContainTextAsync("20 MB", new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task CandidateCvUpload_EditPage_HasNameHelp_SelectionAnnouncement_AndErrorAlert()
    {
        await OpenSeededCandidateAsync();
        var input = _page.Locator("#candidate-cv-file-input");

        await Assertions.Expect(input).ToBeAttachedAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(_page.GetByLabel("CV file (PDF)")).ToHaveAttributeAsync("id", "candidate-cv-file-input");
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-describedby", new Regex("candidate-cv-help"));
        await Assertions.Expect(_page.Locator("#candidate-cv-help")).ToContainTextAsync("PDF");
        await Assertions.Expect(_page.Locator("#candidate-cv-help")).ToContainTextAsync("MB");

        var fileName = $"e2e-a11y-cv-{Guid.NewGuid():N}.pdf";
        await input.SetInputFilesAsync(new FilePayload
        {
            Name = fileName,
            MimeType = "application/pdf",
            Buffer = CandidateCvApi.BuildTestPdf(),
        });
        await Assertions.Expect(_page.Locator("[role='status']").Filter(new() { HasText = $"Selected CV: {fileName}" }))
            .ToHaveCountAsync(1, new() { Timeout = 15_000 });

        await input.SetInputFilesAsync(new FilePayload
        {
            Name = $"e2e-not-a-cv-{Guid.NewGuid():N}.txt",
            MimeType = "text/plain",
            Buffer = "plain text"u8.ToArray(),
        });
        var error = _page.Locator("#candidate-cv-input-error");
        await Assertions.Expect(error).ToHaveAttributeAsync("role", "alert", new() { Timeout = 15_000 });
        await Assertions.Expect(error).ToContainTextAsync("PDF file");
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-describedby", new Regex("candidate-cv-input-error"));
    }

    // ── External recruiters ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExternalRecruiterForm_FieldsAreLabelled_WithEmailAndTelTypes()
    {
        await OpenKeyPageAsync("recruiter-new");

        await Assertions.Expect(_page.GetByLabel("Agency Name")).ToHaveAttributeAsync("id", "recruiter-agency-name");
        await Assertions.Expect(_page.GetByLabel("Contact Name")).ToHaveAttributeAsync("id", "recruiter-contact-name");
        await Assertions.Expect(_page.GetByLabel("Contact Email")).ToHaveAttributeAsync("id", "recruiter-contact-email");
        await Assertions.Expect(_page.GetByLabel("Contact Telephone")).ToHaveAttributeAsync("id", "recruiter-contact-telephone");
        await Assertions.Expect(_page.GetByLabel("Website")).ToHaveAttributeAsync("id", "recruiter-website");
        await Assertions.Expect(_page.GetByLabel("Notes")).ToHaveAttributeAsync("id", "recruiter-notes");

        await A11yAssert.RequiredAsync(_page.Locator("#recruiter-agency-name"));
        await A11yAssert.InputAttributesAsync(_page.Locator("#recruiter-contact-email"), "email", null);
        await A11yAssert.InputAttributesAsync(_page.Locator("#recruiter-contact-telephone"), "tel", null);
    }

    [Fact]
    public async Task ExternalRecruiterForm_EmptySubmit_AssociatesErrorAndFocusesFirstInvalid()
    {
        await OpenKeyPageAsync("recruiter-new");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await A11yAssert.InvalidWithAssociatedMessageAsync(_page, _page.Locator("#recruiter-agency-name"));
        await A11yAssert.FocusIsInsideFieldAsync(_page, "Agency Name");
    }

    [Fact]
    public async Task ExternalRecruiterList_SearchIsNamed_AndAnnouncesResultCount()
    {
        using (var seed = await RecruitmentSeedApi.CreateAsync(_fixture.ApiBaseUrl))
        {
            await seed.CreateExternalRecruiterAsync($"E2E A11y Search Agency {Guid.NewGuid().ToString("N")[..8]}");
        }

        await OpenKeyPageAsync("recruiter-list");

        var search = _page.GetByRole(AriaRole.Textbox, new() { Name = "Search external recruiters", Exact = true });
        await Assertions.Expect(search).ToBeVisibleAsync();
        await Assertions.Expect(Announcer).ToHaveAttributeAsync("role", "status");
        await Assertions.Expect(Announcer).ToHaveAttributeAsync("aria-live", "polite");

        await search.FillAsync($"zzz-no-match-{Guid.NewGuid():N}");
        await _page.Keyboard.PressAsync("Tab");

        await Assertions.Expect(Announcer).ToHaveTextAsync("No recruiters found", new() { Timeout = 20_000 });
    }

    // ── Candidate and vacancy lists ─────────────────────────────────────────────

    [Fact]
    public async Task CandidateList_SearchIsNamed_AndAnnouncesResultCount()
    {
        await OpenKeyPageAsync("candidate-list");

        var search = _page.GetByRole(AriaRole.Textbox, new() { Name = "Search candidates", Exact = true });
        await Assertions.Expect(search).ToBeVisibleAsync();

        await search.FillAsync($"zzz-no-match-{Guid.NewGuid():N}");
        await _page.Keyboard.PressAsync("Tab");

        await Assertions.Expect(Announcer).ToHaveTextAsync("No candidates found", new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task VacancyList_SearchIsNamed_AndAnnouncesResultCount()
    {
        await OpenKeyPageAsync("vacancy-list");

        var search = _page.GetByRole(AriaRole.Textbox, new() { Name = "Search vacancies by title or position", Exact = true });
        await Assertions.Expect(search).ToBeVisibleAsync();

        await search.FillAsync($"zzz-no-match-{Guid.NewGuid():N}");
        await _page.Keyboard.PressAsync("Tab");

        await Assertions.Expect(Announcer).ToHaveTextAsync("No vacancies found", new() { Timeout = 20_000 });
    }

    // ── Reports and filters ─────────────────────────────────────────────────────

    [Fact]
    public async Task RecruitmentPipelineReport_FiltersAreLabelled()
    {
        await OpenKeyPageAsync("report-pipeline");

        await A11yAssert.LabelledAsync(_page, "report-group-by", "Group by");
        await A11yAssert.LabelledAsync(_page, "report-application-type-filter", "Applications");
        await AssertLabelledGroupAsync("rf-");
    }

    [Fact]
    public async Task RecruitmentPipelineSummaryReport_FiltersAreLabelled()
    {
        await OpenKeyPageAsync("report-summary");

        await A11yAssert.LabelledAsync(_page, "report-include-closed", "Include closed vacancies");
        await A11yAssert.LabelledAsync(_page, "report-application-type-filter", "Applications");
    }

    [Fact]
    public async Task VacancyPerformanceReport_FiltersAreLabelled()
    {
        await OpenKeyPageAsync("report-performance");

        await A11yAssert.LabelledAsync(_page, "report-application-type-filter", "Applications");
        await AssertLabelledGroupAsync("rf-");
    }

    [Fact]
    public async Task WorkloadActionsReport_FiltersAreLabelled()
    {
        await LoginAsync(LauraEmail);
        await _page.GotoAsync(Url("reporting/workload-actions"));
        await Assertions.Expect(_page.Locator("label[for='wl-status']")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await AssertLabelledGroupAsync("wl-");
    }

    private async Task AssertLabelledGroupAsync(string idPrefix)
    {
        var labels = _page.Locator($"label[for^='{idPrefix}']");
        await Assertions.Expect(labels.First).ToBeAttachedAsync(new() { Timeout = 30_000 });
        var count = await labels.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var id = await labels.Nth(i).GetAttributeAsync("for");
            var text = ((await labels.Nth(i).TextContentAsync()) ?? "").Trim();
            await Assertions.Expect(_page.Locator($"#{id}")).ToHaveCountAsync(1);
            await Assertions.Expect(_page.Locator($"#{id}")).ToHaveAccessibleNameAsync(text);
        }
    }

    // ── Page-wide checks ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("dashboard")]
    [InlineData("vacancy-list")]
    [InlineData("vacancy-new")]
    [InlineData("vacancy-edit")]
    [InlineData("vacancy-view")]
    [InlineData("candidate-list")]
    [InlineData("candidate-new")]
    [InlineData("candidate-edit")]
    [InlineData("recruiter-list")]
    [InlineData("recruiter-new")]
    [InlineData("recruiter-edit")]
    [InlineData("report-pipeline")]
    [InlineData("report-summary")]
    [InlineData("report-performance")]
    public async Task Page_HasNoControlNamedWithAGenericWidgetType(string key)
    {
        await OpenKeyPageAsync(key);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await A11yAssert.NoGenericControlNamesAsync(_page, key);
    }

    [Theory]
    [InlineData("dashboard")]
    [InlineData("vacancy-list")]
    [InlineData("vacancy-new")]
    [InlineData("vacancy-edit")]
    [InlineData("candidate-list")]
    [InlineData("candidate-new")]
    [InlineData("candidate-edit")]
    [InlineData("recruiter-list")]
    [InlineData("recruiter-new")]
    public async Task Page_HasNoSeriousOrCriticalAxeViolations(string key)
    {
        await OpenKeyPageAsync(key);

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, $"recruitment page {key}");
    }
}
