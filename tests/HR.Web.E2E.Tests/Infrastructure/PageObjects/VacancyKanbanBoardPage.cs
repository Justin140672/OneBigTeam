using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the recruitment Kanban board (VacancyKanbanBoard.razor), which renders in three
/// places with the same markup: the standalone route
/// (/companies/{companyId}/vacancies/{vacancyId}/kanban, via VacancyKanbanPage.razor), the "Kanban"
/// tab on VacancyDetail.razor, and embedded in the Recruitment Dashboard's Board view
/// (RecruitmentDashboard.razor). Callers navigate via whichever entry point their test needs
/// (GoToStandaloneAsync here, or VacancyDetailPage.OpenKanbanTabAsync / RecruitmentDashboardPage),
/// then use the rest of this page object once the board itself is on screen.
///
/// Column/card structure: a plain HTML5 drag-and-drop board (VacancyKanbanBoard.razor) — replaced
/// an earlier Syncfusion SfKanban-based implementation whose own JS drag engine couldn't reliably
/// complete a drop under Blazor Server's re-render cycle. Each column is a
/// "[data-testid='kanban-column']" div carrying its stage name in ".vacancy-kanban-board__column-title"
/// and its card count in ".vacancy-kanban-board__column-count"; cards are native
/// draggable="true" wrappers around our own KanbanApplicantCard.razor template, whose outer element
/// carries the well-known "kanban-applicant-card" class (plus a stage-specific modifier — see
/// StageCssClassAsync).
/// </summary>
public sealed class VacancyKanbanBoardPage(IPage page, string baseUrl)
{
    private ILocator Board => page.Locator("[data-testid='vacancy-kanban-board']");

    public async Task GoToStandaloneAsync(Guid companyId, Guid vacancyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/vacancies/{vacancyId}/kanban");
        await WaitForLoadedAsync();
    }

    /// <summary>
    /// Waits until the board itself has finished its initial load (the HrLoadingIndicator spinner
    /// shown while _loading is true has been replaced by the actual columns).
    /// </summary>
    public async Task WaitForLoadedAsync()
    {
        await page.WaitForSelectorAsync("[data-testid='vacancy-kanban-board']", new() { Timeout = 20_000 });
        await page.WaitForSelectorAsync(
            "[data-testid='vacancy-kanban-board'] [data-testid='kanban-columns']",
            new() { Timeout = 20_000 });
    }

    // ── Search box (ticket #69) ──────────────────────────────────────────────────

    // Tolerant of either DOM shape Syncfusion's SfTextBox might render the data-testid onto: the
    // attribute could land directly on the <input> itself, or on a wrapper (e.g. ".e-input-group")
    // containing a nested <input> — this was never confirmed against a live board before this file
    // was written, so match both rather than assuming one.
    //
    // VacancyKanbanBoard.razor's SfTextBox only raises ValueChange (which FilteredCards depends on)
    // on blur/change, not on Playwright's FillAsync-dispatched "input" event alone — same recurring
    // gotcha as every other SfTextBox-driven search box in this suite (e.g.
    // ReportCatalogPage.SearchAsync, UserAdministrationListPage's search). An explicit Tab forces
    // the blur so the filter actually applies before the caller asserts on it.
    public async Task FillSearchAsync(string text)
    {
        var input = Board.Locator("input[data-testid='kanban-search-box'], [data-testid='kanban-search-box'] input").First;
        await input.FillAsync(text);
        await input.PressAsync("Tab");

        // ValueChange only fires on blur (see comment above), and the resulting FilteredCards
        // re-render is a server round-trip (InteractiveServer render mode) rather than an instant
        // client-side re-filter — CountVisibleCardsAsync/HasCardForNameAsync read the DOM
        // synchronously with no auto-retry, so a caller right after this method returns can
        // otherwise race the pre-filter card set.
        await Board.Page.WaitForTimeoutAsync(400);
    }

    public async Task<int> CountVisibleCardsAsync() =>
        await Board.Locator(".kanban-applicant-card").CountAsync();

    public async Task<bool> HasCardForNameAsync(string candidateNameFragment) =>
        await Board.Locator(".kanban-applicant-card").Filter(new() { HasText = candidateNameFragment }).CountAsync() > 0;

    // ── Columns (tickets #69/#71) ────────────────────────────────────────────────

    /// <summary>
    /// Resolves the column position of the title text matching <paramref name="stageName"/> exactly
    /// (not a substring match — a candidate's own card content, e.g. a name that happens to contain
    /// a stage name, must never accidentally match a column title). Index-based rather than a
    /// Playwright Filter(Has:) chain: that pattern proved unreliable here (every column lookup was
    /// silently resolving to zero elements), whereas indexing the plain title-and-column lists in
    /// lockstep is the same reliable approach this suite already used against the previous
    /// Syncfusion-based DOM.
    /// </summary>
    private async Task<int> GetColumnIndexAsync(string stageName)
    {
        var titles = Board.Locator(".vacancy-kanban-board__column-title");
        var count = await titles.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var text = ((await titles.Nth(i).TextContentAsync()) ?? "").Trim();
            if (text.Equals(stageName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private async Task<ILocator?> TryColumnAsync(string stageName)
    {
        var index = await GetColumnIndexAsync(stageName);
        return index < 0 ? null : Board.Locator("[data-testid='kanban-column']").Nth(index);
    }

    private async Task<ILocator> ColumnAsync(string stageName) =>
        await TryColumnAsync(stageName)
            ?? throw new InvalidOperationException($"Could not find a Kanban column for stage '{stageName}'.");

    /// <summary>
    /// Returns true if the Kanban column header for <paramref name="stageName"/> is visible. Polls
    /// briefly rather than taking a single instant snapshot — WaitForLoadedAsync only confirms the
    /// board shell itself exists, not that all of its (up to six) columns have individually finished
    /// rendering yet.
    /// </summary>
    public async Task<bool> HasColumnHeaderAsync(string stageName)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await TryColumnAsync(stageName) is not null)
                return true;
            await page.WaitForTimeoutAsync(200);
        }

        return false;
    }

    /// <summary>
    /// Reads the card-count badge (".vacancy-kanban-board__column-count") for the column headed by
    /// <paramref name="stageName"/>.
    /// </summary>
    public async Task<int> GetColumnCountAsync(string stageName)
    {
        var column = await ColumnAsync(stageName);
        var text = (await column.Locator(".vacancy-kanban-board__column-count").TextContentAsync()) ?? "";
        return int.TryParse(text.Trim(), out var parsed) ? parsed : 0;
    }

    /// <summary>
    /// Returns true if the card matching <paramref name="candidateNameFragment"/> currently sits in
    /// the column headed by <paramref name="stageName"/> — used to confirm which column a card
    /// actually landed in after a drag (or after a fresh page load/re-navigation, to prove a drag's
    /// server-side effect actually persisted rather than being a client-only visual move).
    /// </summary>
    public async Task<bool> IsCardInColumnAsync(string candidateNameFragment, string stageName)
    {
        var column = await ColumnAsync(stageName);
        return await column.Locator(".kanban-applicant-card").Filter(new() { HasText = candidateNameFragment }).CountAsync() > 0;
    }

    // ── Card content / visual distinction (ticket #71) ───────────────────────────

    private ILocator Card(string candidateNameFragment) =>
        Board.Locator(".kanban-applicant-card").Filter(new() { HasText = candidateNameFragment }).First;

    public Task<bool> IsCardVisibleAsync(string candidateNameFragment) => Card(candidateNameFragment).IsVisibleAsync();

    public async Task<string> GetCardClassAsync(string candidateNameFragment) =>
        (await Card(candidateNameFragment).GetAttributeAsync("class")) ?? "";

    public Task<bool> HasRejectedStylingAsync(string candidateNameFragment) =>
        HasClassAsync(candidateNameFragment, "kanban-applicant-card--danger");

    public Task<bool> HasHiredStylingAsync(string candidateNameFragment) =>
        HasClassAsync(candidateNameFragment, "kanban-applicant-card--success");

    private async Task<bool> HasClassAsync(string candidateNameFragment, string cssClass) =>
        (await GetCardClassAsync(candidateNameFragment)).Split(' ').Contains(cssClass);

    public async Task<string?> GetCardStatusBadgeTextAsync(string candidateNameFragment)
    {
        var badge = Card(candidateNameFragment).Locator(".status-badge, .badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    public async Task<string?> GetCardRecruiterTextAsync(string candidateNameFragment)
    {
        var meta = Card(candidateNameFragment).Locator(".kanban-applicant-card__meta");
        return (await meta.TextContentAsync())?.Trim();
    }

    // ── Card click → navigate to candidate detail (ticket #71) ──────────────────

    public Task ClickCardAsync(string candidateNameFragment) => Card(candidateNameFragment).ClickAsync();

    // ── Drag and drop between columns (ticket #72) ───────────────────────────────
    // The board now uses native HTML5 drag-and-drop (draggable="true" cards, dragover/drop on each
    // column) rather than a JS-widget-owned pointer drag, so Playwright's own DragToAsync — which
    // dispatches the real dragstart/dragenter/dragover/drop DOM event sequence — is recognized
    // directly; no manual mouse down/move/up choreography is needed anymore.
    public async Task DragCardToColumnAsync(string candidateNameFragment, string targetStageName)
    {
        var card = Card(candidateNameFragment);
        await card.ScrollIntoViewIfNeededAsync();

        var column = await ColumnAsync(targetStageName);
        var targetColumn = column.Locator(".vacancy-kanban-board__column-content");
        await targetColumn.ScrollIntoViewIfNeededAsync();

        await card.DragToAsync(targetColumn);

        // The drop handler (OnDropAsync) always re-fetches the board afterward, whether the move
        // was accepted or rejected by the server — give that round-trip a moment to settle before
        // the caller asserts on the resulting column membership.
        await page.WaitForTimeoutAsync(500);
    }

    // ── Error banner ──────────────────────────────────────────────────────────────

    public Task<bool> IsErrorVisibleAsync() => Board.Locator("[data-testid='kanban-error']").IsVisibleAsync();

    public async Task<string?> GetErrorTextAsync()
    {
        var error = Board.Locator("[data-testid='kanban-error']");
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }

    // ── Empty-column copy (redesign) ─────────────────────────────────────────────

    /// <summary>
    /// Returns the empty-state text (".vacancy-kanban-board__column-empty") shown inside the column
    /// headed by <paramref name="stageName"/> when it has zero cards, or null if the column currently
    /// has at least one card (and so never renders the empty-state div at all).
    /// </summary>
    public async Task<string?> GetColumnEmptyTextAsync(string stageName)
    {
        var column = await ColumnAsync(stageName);
        var empty = column.Locator(".vacancy-kanban-board__column-empty");
        return await empty.IsVisibleAsync() ? (await empty.TextContentAsync())?.Trim() : null;
    }

    // ── Drop-target "dragging" highlight (redesign) ──────────────────────────────

    private ILocator ColumnsContainer => Board.Locator("[data-testid='kanban-columns']");

    /// <summary>
    /// True while the board's outer columns container carries the "dragging" CSS class, which
    /// VacancyKanbanBoard.razor toggles on in OnDragStart and off in OnDragEnd — used to assert the
    /// class's lifecycle around a drag rather than any actual visual highlight (that part is pure
    /// CSS :hover and not meaningfully assertable via Playwright without visual regression tooling).
    /// </summary>
    public async Task<bool> HasDraggingClassAsync() =>
        ((await ColumnsContainer.GetAttributeAsync("class")) ?? "").Split(' ').Contains("dragging");

    /// <summary>
    /// Manually dispatches a "dragstart" event on the given card (bypassing Playwright's
    /// DragToAsync, which completes the whole drag+drop sequence too fast to observe the
    /// intermediate "dragging" class) so the caller can assert the class is present mid-drag, then
    /// dispatches "dragend" to return the board to its rest state without ever dropping the card
    /// onto a column (so no server-side move happens as a side effect of this helper).
    /// </summary>
    public async Task<bool> ObserveDraggingClassDuringManualDragAsync(string candidateNameFragment)
    {
        var card = Card(candidateNameFragment);
        await card.ScrollIntoViewIfNeededAsync();

        // A DataTransfer instance is required for a realistic dragstart dispatch even though this
        // board's handlers don't read anything off it.
        await card.EvaluateAsync(@"el => {
            const dt = new DataTransfer();
            el.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer: dt }));
        }");

        var duringDrag = await HasDraggingClassAsync();

        await card.EvaluateAsync(@"el => {
            const dt = new DataTransfer();
            el.dispatchEvent(new DragEvent('dragend', { bubbles: true, cancelable: true, dataTransfer: dt }));
        }");

        return duringDrag;
    }

    // ── "Move to stage…" card menu (redesign, keyboard-accessible alternative to drag) ──────────

    private ILocator MoveStageButton(string candidateNameFragment) =>
        Card(candidateNameFragment).Locator("[data-testid='kanban-card-move-stage-btn']");

    private ILocator MoveStageMenu(string candidateNameFragment) =>
        Card(candidateNameFragment).Locator("[data-testid='kanban-card-move-stage-menu']");

    private ILocator MoveStageMenuItem(string candidateNameFragment, string targetStageName) =>
        MoveStageMenu(candidateNameFragment).GetByRole(AriaRole.Menuitem, new() { Name = targetStageName, Exact = true });

    /// <summary>
    /// Mouse-driven path: click the card's kebab "Move to stage…" button, then click the named
    /// stage's menu item.
    /// </summary>
    public async Task MoveToStageViaMouseAsync(string candidateNameFragment, string targetStageName)
    {
        await MoveStageButton(candidateNameFragment).ClickAsync();
        await MoveStageMenu(candidateNameFragment).WaitForAsync(new() { Timeout = 10_000 });
        await MoveStageMenuItem(candidateNameFragment, targetStageName).ClickAsync();

        // MoveApplicationAsync always reloads the board afterward (success or failure) — mirror the
        // same settle delay DragCardToColumnAsync uses before the caller asserts on the outcome.
        await page.WaitForTimeoutAsync(500);
    }

    /// <summary>
    /// Keyboard-only path: focuses the card's kebab "Move to stage…" button directly (simulating
    /// having tabbed to it) and activates it with Enter, then focuses the target stage's menu item
    /// and activates it with Enter — no mouse clicks anywhere in this method.
    /// </summary>
    public async Task MoveToStageViaKeyboardAsync(string candidateNameFragment, string targetStageName)
    {
        var button = MoveStageButton(candidateNameFragment);
        await button.FocusAsync();
        await button.PressAsync("Enter");

        var menuItem = MoveStageMenuItem(candidateNameFragment, targetStageName);
        await menuItem.WaitForAsync(new() { Timeout = 10_000 });
        await menuItem.FocusAsync();
        await menuItem.PressAsync("Enter");

        await page.WaitForTimeoutAsync(500);
    }

    public Task<bool> IsMoveStageMenuOpenAsync(string candidateNameFragment) =>
        MoveStageMenu(candidateNameFragment).IsVisibleAsync();
}
