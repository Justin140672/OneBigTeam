using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Recruitment Stages settings page (RecruitmentStageList.razor / RecruitmentStageEdit.razor,
/// ticket #100) — create, edit, reorder (move-up/move-down), and activate/deactivate a stage.
///
/// Uses the seeded Acme company (00000000-0000-0000-0000-000000000001) which already has
/// RecruitmentStageSeeder's six default stages (Application Received, CV Review, Interview, Offer,
/// Hired, Rejected) at DisplayOrder 1-6 the first time any recruitment data exists for the company —
/// per the seeder's own comment this happens the moment the first Vacancy is created, which other
/// test classes (e.g. VacancyKanbanBoardTests) will have already triggered against this shared
/// database by the time these tests run. Tests only add/rename/move newly-created stages (unique
/// names per run) so they don't collide with each other or depend on ordering among themselves.
///
/// Every test here mutates the single shared, DisplayOrder-ranked list of Acme recruitment
/// pipeline stages (create at the end, reorder up/down, deactivate) — anything elsewhere that
/// reads that list by stage name/position races it. In particular ApplicationToEmployeeFlowTests
/// (CrossUserVacancyTestBase group) asserts the candidate lands on the "Offer" stage by reading
/// the pipeline's current stage list; if it reads that list while this class's newly-created
/// "E2E Stage Reorder …" stage is still active and hasn't been moved back/deactivated yet, "Offer"
/// can be displaced. This class can't join CrossUserVacancyTestBase directly (different fixture —
/// RecruiterPersonaFixture, not CrossUserFixture), so it serializes against that group's shared
/// static gate instance directly instead, via its own IAsyncLifetime override below.
/// </summary>
public sealed class RecruitmentStageManagementTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    public override async Task InitializeAsync()
    {
        await CrossUserVacancyTestBase.GateInstance.WaitAsync();
        await base.InitializeAsync();
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

    /// <summary>
    /// Deactivates any leftover active "E2E Stage ..." row still sitting in the list from an
    /// earlier run — the try/finally guards added to every test below only stop *new* pollution;
    /// they can't retroactively fix a stray stage stranded by a run that predates those guards (or
    /// by any other not-yet-understood failure mode). Since every stage this class creates is
    /// uniquely named per run but always shares the "E2E Stage" prefix, and this class's own tests
    /// are the only thing in the whole suite that ever creates one (see class doc comment), ANY
    /// active "E2E Stage"-prefixed row found here at the start of a test — before this run has
    /// created its own — is unconditionally a stray from a previous run. Self-heals the shared,
    /// long-lived E2E dev database the same way ReportCatalogTests' favourite-toggle tests do,
    /// rather than assuming a clean starting state that a `try/finally` alone can't guarantee.
    ///
    /// A Hired/Rejected-outcome stray is only deactivated if at least one OTHER active stage
    /// shares that same outcome (i.e. deactivating it is verifiably safe under
    /// SetRecruitmentStageActiveStatusHandler's "at least one active stage per terminal outcome"
    /// rule — the same check that handler itself performs server-side, done here first so this
    /// cleanup never attempts, let alone risks, tipping the company down to zero active stages for
    /// an outcome). Round 8's version skipped every Hired/Rejected-outcome stray unconditionally,
    /// which was safe but meant a stray of that shape could never be cleaned up at all, leaving it
    /// permanently stuck in the list (and permanently shifting order-based assertions like
    /// ReorderStage_MoveUpAndDown_PersistsAcrossReload's index math) — this round replaces that
    /// blanket skip with the actual safety check so a genuinely-safe-to-remove stray gets removed,
    /// while a real "this is the only one left" case still isn't touched.
    /// </summary>
    private static async Task CleanupStrayStagesAsync(RecruitmentStageListPage stageList)
    {
        var names = await stageList.GetNamesInOrderAsync();
        var activeNamesByOutcome = new Dictionary<string, List<string>>();
        foreach (var name in names.Distinct())
        {
            if (!await stageList.IsActiveAsync(name)) continue;
            var outcome = await stageList.GetTerminalOutcomeAsync(name) ?? "None";
            if (!activeNamesByOutcome.TryGetValue(outcome, out var list))
                activeNamesByOutcome[outcome] = list = [];
            list.Add(name);
        }

        foreach (var name in names.Distinct())
        {
            if (!name.StartsWith("E2E Stage", StringComparison.Ordinal)) continue;
            if (!await stageList.IsActiveAsync(name)) continue;

            var outcome = await stageList.GetTerminalOutcomeAsync(name) ?? "None";
            if (outcome is "Hired" or "Rejected")
            {
                var othersWithSameOutcome = activeNamesByOutcome.TryGetValue(outcome, out var list)
                    ? list.Count(n => n != name)
                    : 0;
                if (othersWithSameOutcome == 0) continue;
            }

            await stageList.DeactivateAsync(name);
            if (activeNamesByOutcome.TryGetValue(outcome, out var mutated))
                mutated.Remove(name);
        }
    }

    [Fact]
    public async Task CreateRecruitmentStage_AppearsInList()
    {
        var stageName = $"E2E Stage {Guid.NewGuid().ToString("N")[..8]}";

        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var stageList = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);
        var stageEdit = new RecruitmentStageEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await stageList.GoToAsync(AcmeId);
        await CleanupStrayStagesAsync(stageList);
        await stageList.ClickNewAsync();

        await stageEdit.FillNameAsync(stageName);
        // Leave Terminal Outcome at its default ("None") — a plain non-terminal stage.
        await stageEdit.SaveAsync();

        // A freshly created stage is appended to the end of the list, i.e. it gets the HIGHEST
        // DisplayOrder of any stage in the company. OfferCandidateHandler picks the active
        // non-terminal stage with the highest DisplayOrder when moving a candidate to "Offer", so
        // leaving this stage active would silently outrank the seeded "Offer" stage and break
        // ApplicationToEmployeeFlowTests (and any other test relying on offers landing on "Offer").
        // Deactivate it once this test's own assertions are done so it can never be picked — guarded
        // by try/finally so an assertion failure below still cleans it up: an un-deactivated stray
        // stage here doesn't just affect THIS run, it permanently pollutes every future run against
        // the shared, long-lived E2E dev database (e.g. shifting index-based order assertions in
        // ReorderStage_MoveUpAndDown_PersistsAcrossReload).
        try
        {
            Assert.True(await stageList.HasItemAsync(stageName),
                $"Expected the new recruitment stage '{stageName}' to appear in the list after creation");
        }
        finally
        {
            await stageList.GoToAsync(AcmeId);
            await stageList.DeactivateAsync(stageName);
        }
    }

    [Fact]
    public async Task EditRecruitmentStage_NameAndTerminalOutcome_PersistAcrossReload()
    {
        var originalName = $"E2E Stage Edit {Guid.NewGuid().ToString("N")[..8]}";
        var updatedName  = $"{originalName} Updated";

        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var stageList = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);
        var stageEdit = new RecruitmentStageEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await stageList.GoToAsync(AcmeId);
        await CleanupStrayStagesAsync(stageList);
        await stageList.ClickNewAsync();
        await stageEdit.FillNameAsync(originalName);
        await stageEdit.SaveAsync();

        await stageList.GoToAsync(AcmeId);
        await stageList.ClickRowLinkAsync(originalName);
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        // "None" rather than "Hired"/"Rejected" — UpdateRecruitmentStageHandler enforces that only
        // one active stage per company may hold a given terminal outcome, and RecruitmentStageSeeder
        // always seeds one active Hired and one active Rejected stage already, so either would be
        // rejected here (see AssertOnlyActiveTerminalStageCannotBeDeactivatedAsync's doc comment for
        // the full rule). "None" has no such uniqueness constraint and still exercises the dropdown
        // selection and its persistence across reload.
        await stageEdit.FillNameAsync(updatedName);
        await stageEdit.SelectTerminalOutcomeAsync("None");
        await stageEdit.SaveAsync();

        // See CreateRecruitmentStage_AppearsInList's comment: a stage left active here (with the
        // highest DisplayOrder in the company) can silently hijack OfferCandidateHandler's stage
        // selection for unrelated tests — guarded by try/finally so an assertion failure below still
        // deactivates it instead of permanently polluting every future run.
        try
        {
            await stageList.GoToAsync(AcmeId);
            Assert.True(await stageList.HasItemAsync(updatedName),
                "Expected the renamed stage to appear in the list");
            Assert.Equal("None", await stageList.GetTerminalOutcomeAsync(updatedName));

            // Reload to confirm the change persisted server-side, not just in local component state.
            await stageList.ClickRowLinkAsync(updatedName);
            await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
            await _page.ReloadAsync();
            await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

            Assert.Equal(updatedName, await stageEdit.GetNameAsync());
        }
        finally
        {
            await stageList.GoToAsync(AcmeId);
            await stageList.DeactivateAsync(updatedName);
        }
    }

    [Fact]
    public async Task ReorderStage_MoveUpAndDown_PersistsAcrossReload()
    {
        var stageName = $"E2E Stage Reorder {Guid.NewGuid().ToString("N")[..8]}";

        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var stageList = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);
        var stageEdit = new RecruitmentStageEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // A freshly created stage is appended to the end of the list (RecruitmentStageService's
        // IEditService.CreateAsync sets DisplayOrder = existingCount + 1), so it starts as the last
        // row and can always be moved up at least once regardless of how many stages already exist.
        await stageList.GoToAsync(AcmeId);
        await CleanupStrayStagesAsync(stageList);
        await stageList.ClickNewAsync();
        await stageEdit.FillNameAsync(stageName);
        await stageEdit.SaveAsync();

        // See CreateRecruitmentStage_AppearsInList's comment: guarded by try/finally so an
        // assertion failure anywhere below still deactivates this stage — an un-deactivated
        // stray "E2E Stage Reorder …" here doesn't just fail THIS run, it permanently shifts
        // index-based order assertions (in this test and others) on every future run against the
        // shared, long-lived E2E dev database, since a leftover active stage changes every
        // subsequent test's starting stage count/order.
        try
        {
            await stageList.GoToAsync(AcmeId);
            var namesBefore = await stageList.GetNamesInOrderAsync();
            var indexBefore = namesBefore.ToList().IndexOf(stageName);
            Assert.True(indexBefore > 0, $"Expected the newly created stage '{stageName}' to not already be first in the list");

            await stageList.MoveUpAsync(stageName);

            var namesAfterUp = await stageList.GetNamesInOrderAsync();
            var indexAfterUp = namesAfterUp.ToList().IndexOf(stageName);
            Assert.Equal(indexBefore - 1, indexAfterUp);

            // Reload the page directly (fresh navigation) to confirm the reorder was persisted
            // server-side (ReorderAsync), not just reflected in local grid state.
            await stageList.GoToAsync(AcmeId);
            var namesAfterReload = await stageList.GetNamesInOrderAsync();
            Assert.Equal(indexAfterUp, namesAfterReload.ToList().IndexOf(stageName));

            // Move back down — should return to its original position.
            await stageList.MoveDownAsync(stageName);
            var namesAfterDown = await stageList.GetNamesInOrderAsync();
            Assert.Equal(indexBefore, namesAfterDown.ToList().IndexOf(stageName));
        }
        finally
        {
            await stageList.GoToAsync(AcmeId);
            await stageList.DeactivateAsync(stageName);
        }
    }

    [Fact]
    public async Task DeactivateThenReactivateStage_TogglesActiveBadge()
    {
        var stageName = $"E2E Stage Deact {Guid.NewGuid().ToString("N")[..8]}";

        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var stageList = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);
        var stageEdit = new RecruitmentStageEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await stageList.GoToAsync(AcmeId);
        await CleanupStrayStagesAsync(stageList);
        await stageList.ClickNewAsync();
        await stageEdit.FillNameAsync(stageName);
        await stageEdit.SaveAsync();

        // See CreateRecruitmentStage_AppearsInList's comment: this test's assertions specifically
        // require ending in the Active state, so the final deactivate can only happen after
        // they're complete — guarded by try/finally so an assertion failure midway still leaves
        // the stage deactivated instead of permanently polluting every future run.
        try
        {
            await stageList.GoToAsync(AcmeId);
            Assert.True(await stageList.IsActiveAsync(stageName), "Expected newly created stage to be Active");
            await stageList.DeactivateAsync(stageName);

            await stageList.ShowInactiveAsync();
            Assert.True(await stageList.HasItemAsync(stageName),
                "Expected deactivated stage to appear when 'Show inactive' is enabled");
            Assert.False(await stageList.IsActiveAsync(stageName), "Expected the stage to now show as Inactive");

            await stageList.ActivateAsync(stageName);
            Assert.True(await stageList.IsActiveAsync(stageName), "Expected the stage to be Active again after reactivation");
        }
        finally
        {
            await stageList.DeactivateAsync(stageName);
        }
    }

    [Fact]
    public async Task CreateRecruitmentStage_WithDuplicateName_ShowsValidationError()
    {
        var stageName = $"E2E Stage Dup {Guid.NewGuid().ToString("N")[..8]}";

        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var stageList = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);
        var stageEdit = new RecruitmentStageEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await stageList.GoToAsync(AcmeId);
        await CleanupStrayStagesAsync(stageList);
        await stageList.ClickNewAsync();
        await stageEdit.FillNameAsync(stageName);
        await stageEdit.SaveAsync();

        // See CreateRecruitmentStage_AppearsInList's comment: the first (successfully created)
        // stage above is still active with the highest DisplayOrder in the company — guarded by
        // try/finally so an assertion failure below still deactivates it.
        try
        {
            // Attempt to create a second stage with the exact same name for the same company.
            await stageList.GoToAsync(AcmeId);
            await stageList.ClickNewAsync();
            await stageEdit.FillNameAsync(stageName);
            await stageEdit.ClickSaveExpectingErrorAsync();

            Assert.True(await stageEdit.HasErrorAsync(),
                "Expected a validation error banner when saving a recruitment stage with a duplicate name");
        }
        finally
        {
            await stageList.GoToAsync(AcmeId);
            await stageList.DeactivateAsync(stageName);
        }
    }

    /// <summary>
    /// Covers SetRecruitmentStageActiveStatusHandler's rule that the only active stage with a given
    /// TerminalOutcome (Hired/Rejected) can never be deactivated — see the handler's comment for the
    /// full rationale (HireCandidate/RejectCandidate would otherwise have nowhere to move
    /// applications). This case was originally skipped because the Acme company's seeded stages
    /// (RecruitmentStageSeeder) are shared with every other test in this class/suite, and simply
    /// deactivating "the" active Hired or Rejected stage risked either corrupting that shared setup
    /// (if the attempt unexpectedly succeeded) or spuriously failing (if some other test happened to
    /// have left a second active stage with the same outcome behind — e.g.
    /// EditRecruitmentStage_NameAndTerminalOutcome_PersistAcrossReload deliberately sets a new
    /// stage's outcome to "Rejected" and never deactivates it again).
    ///
    /// Rather than requiring a brand-new isolated company (this codebase has no UI or established
    /// E2E convention for provisioning one — CreateCompany is an API-only feature with no
    /// corresponding page), this test makes itself self-contained against however many active
    /// Hired/Rejected stages already exist: it temporarily deactivates every active stage with the
    /// target outcome except one, runs the assertion against that one, then reactivates whichever
    /// stages it touched in a finally block so the shared company is left exactly as it found it
    /// (the assertion itself never mutates state, since the deactivation under test is expected to
    /// be rejected by the server).
    /// </summary>
    [Fact]
    public async Task DeactivateOnlyActiveHiredStage_ShowsValidationError_AndStageRemainsActive()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var stageList = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await AssertOnlyActiveTerminalStageCannotBeDeactivatedAsync(stageList, "Hired");
    }

    /// <summary>See <see cref="DeactivateOnlyActiveHiredStage_ShowsValidationError_AndStageRemainsActive"/> — same rule, Rejected outcome.</summary>
    [Fact]
    public async Task DeactivateOnlyActiveRejectedStage_ShowsValidationError_AndStageRemainsActive()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var stageList = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await AssertOnlyActiveTerminalStageCannotBeDeactivatedAsync(stageList, "Rejected");
    }

    /// <summary>
    /// NOTE: the third business rule enforced by SetRecruitmentStageActiveStatusHandler — "at least
    /// one active recruitment stage must remain overall" — is not separately exercised here. To
    /// reach that state via the UI, every active Hired and Rejected stage would first need to be
    /// deactivated down to a single non-terminal survivor, but the handler's own terminal-outcome
    /// rule (exercised above) forbids deactivating the last active Hired or Rejected stage in the
    /// first place. So long as this seeded company always has at least one active Hired stage and
    /// one active Rejected stage (true by construction — RecruitmentStageSeeder seeds one of each,
    /// and this suite never deactivates them down to zero), the "last stage overall" branch can
    /// never actually be reached from here without also reaching (and being blocked by) the
    /// terminal-outcome branch on the way down. It's covered directly by the backend integration
    /// test instead.
    /// </summary>
    private async Task AssertOnlyActiveTerminalStageCannotBeDeactivatedAsync(
        RecruitmentStageListPage stageList, string terminalOutcome)
    {
        await stageList.GoToAsync(AcmeId);
        await stageList.ShowInactiveAsync();

        var allNames = await stageList.GetNamesInOrderAsync();
        var activeWithOutcome = new List<string>();
        foreach (var name in allNames.Distinct())
        {
            if (await stageList.IsActiveAsync(name) && await stageList.GetTerminalOutcomeAsync(name) == terminalOutcome)
                activeWithOutcome.Add(name);
        }

        Assert.True(activeWithOutcome.Count > 0,
            $"Expected at least one active recruitment stage with terminal outcome '{terminalOutcome}' to exist");

        var target = activeWithOutcome[0];
        var temporarilyDeactivated = new List<string>();

        try
        {
            // Isolate the assertion below from any other active stage sharing this outcome that
            // other tests in this shared company may have left behind.
            foreach (var extra in activeWithOutcome.Skip(1))
            {
                await stageList.DeactivateAsync(extra);
                temporarilyDeactivated.Add(extra);
            }

            await stageList.DeactivateAsync(target);

            Assert.True(await stageList.HasActionErrorAsync(),
                $"Expected a validation error banner when attempting to deactivate the only active '{terminalOutcome}' stage");
            var errorText = await stageList.GetActionErrorTextAsync();
            Assert.Contains(terminalOutcome, errorText ?? "");

            Assert.True(await stageList.IsActiveAsync(target),
                $"Expected the '{target}' stage to remain active after the rejected deactivation attempt");
        }
        finally
        {
            foreach (var extra in temporarilyDeactivated)
                await stageList.ActivateAsync(extra);
        }
    }
}
