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
/// Column/card structure (Syncfusion Blazor Kanban, SfKanban/KanbanColumn/KanbanCardSettings),
/// confirmed against a live board's DOM: the widget renders a header row with one ".e-header-cells"
/// (plural) per KanbanColumn (containing ".e-header-text" for the HeaderText and ".e-item-count",
/// formatted like "- 1 item"/"- 0 items", for the ShowItemCount badge), and a
/// content row with one ".e-content-cells" per column holding its cards. Card content itself is our
/// own KanbanApplicantCard.razor template, whose outer element carries the well-known
/// "kanban-applicant-card" class (plus a stage-specific modifier — see StageCssClassAsync).
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
    /// shown while _loading is true has been replaced by the actual SfKanban widget).
    /// </summary>
    public async Task WaitForLoadedAsync()
    {
        await page.WaitForSelectorAsync("[data-testid='vacancy-kanban-board']", new() { Timeout = 20_000 });
        await page.WaitForSelectorAsync(
            "[data-testid='vacancy-kanban-board'] .e-kanban, [data-testid='vacancy-kanban-board'] .e-content-cells",
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

    // Confirmed against a live board's rendered DOM: Syncfusion's Kanban header cell is
    // ".e-header-cells" (plural — NOT the singular ".e-header-cell" this used to guess at, which
    // matched nothing and made every header-dependent assertion below fail/time out).
    private ILocator HeaderCell(string stageName) =>
        Board.Locator(".e-header-cells").Filter(new() { HasText = stageName }).First;

    /// <summary>
    /// Returns true if the Kanban column header for <paramref name="stageName"/> is visible.
    /// Polls briefly rather than taking a single IsVisibleAsync() snapshot — WaitForLoadedAsync
    /// only confirms the Kanban widget shell itself exists, not that all of its (up to six)
    /// column headers have individually finished rendering yet.
    /// </summary>
    public async Task<bool> HasColumnHeaderAsync(string stageName)
    {
        try
        {
            await Assertions.Expect(HeaderCell(stageName)).ToBeVisibleAsync(new() { Timeout = 10_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the ShowItemCount badge (".e-item-count") for the column headed by
    /// <paramref name="stageName"/>. Confirmed against a live board: its text is formatted like
    /// "- 1 item" / "- 0 items" (not a bare digit), so this extracts the digits rather than
    /// int.TryParse-ing the whole string directly.
    /// </summary>
    public async Task<int> GetColumnCountAsync(string stageName)
    {
        var header = HeaderCell(stageName);
        var countEl = header.Locator(".e-item-count");
        if (await countEl.CountAsync() > 0)
        {
            var text = (await countEl.First.TextContentAsync()) ?? "";
            var digits = new string(text.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var parsed)) return parsed;
        }

        var full = (await header.TextContentAsync()) ?? "";
        var fullDigits = new string(full.Where(char.IsDigit).ToArray());
        return int.TryParse(fullDigits, out var fallback) ? fallback : 0;
    }

    /// <summary>
    /// Resolves the column position of the header text matching <paramref name="stageName"/> — the
    /// header row and content row share the same column order (ApplicationStatusTransitionRules.
    /// ColumnOrder), so this index is used to find the matching content cell elsewhere (drag/drop,
    /// column-membership checks).
    /// </summary>
    private async Task<int> GetColumnIndexAsync(string stageName)
    {
        var headerCells = Board.Locator(".e-header-cells");
        var headerCount = await headerCells.CountAsync();
        for (var i = 0; i < headerCount; i++)
        {
            var text = (await headerCells.Nth(i).TextContentAsync()) ?? "";
            if (text.Contains(stageName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        throw new InvalidOperationException($"Could not find a Kanban column header for stage '{stageName}'.");
    }

    /// <summary>
    /// Returns true if the card matching <paramref name="candidateNameFragment"/> currently sits in
    /// the content cell of the column headed by <paramref name="stageName"/> — used to confirm which
    /// column a card actually landed in after a drag (or after a fresh page load/re-navigation, to
    /// prove a drag's server-side effect actually persisted rather than being a client-only visual
    /// move).
    /// </summary>
    public async Task<bool> IsCardInColumnAsync(string candidateNameFragment, string stageName)
    {
        var index = await GetColumnIndexAsync(stageName);
        var cell = Board.Locator(".e-content-cells").Nth(index);
        return await cell.Locator(".kanban-applicant-card").Filter(new() { HasText = candidateNameFragment }).CountAsync() > 0;
    }

    // ── Card content / visual distinction (ticket #71) ───────────────────────────

    private ILocator Card(string candidateNameFragment) =>
        Board.Locator(".kanban-applicant-card").Filter(new() { HasText = candidateNameFragment }).First;

    public Task<bool> IsCardVisibleAsync(string candidateNameFragment) => Card(candidateNameFragment).IsVisibleAsync();

    public async Task<string> GetCardClassAsync(string candidateNameFragment) =>
        (await Card(candidateNameFragment).GetAttributeAsync("class")) ?? "";

    public Task<bool> HasWithdrawnStylingAsync(string candidateNameFragment) =>
        HasClassAsync(candidateNameFragment, "kanban-applicant-card--danger");

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
    // Syncfusion's EJ2 Kanban (which SfKanban wraps) implements its own pointer-based drag rather
    // than native HTML5 drag-and-drop, so a plain Locator.DragToAsync (which dispatches
    // dragstart/drop DOM events) is not recognized by the widget. A manual mouse down/move/up
    // sequence — hovering the source card, pressing, moving in incremental steps toward the target
    // column's content area, then releasing — is used instead. Exercised against a live board: the
    // original version (a single pre-drag bounding-box snapshot, straight-line move, no dwell
    // before release) reliably dropped right on the boundary between the target column and its
    // neighbor instead of solidly inside it, which Syncfusion couldn't resolve to a column and just
    // reverted the card — see the re-measure/dwell comments in DragCardToColumnAsync below for what
    // that fixed.
    public async Task DragCardToColumnAsync(string candidateNameFragment, string targetStageName)
    {
        var card = Card(candidateNameFragment);
        await card.ScrollIntoViewIfNeededAsync();
        var cardBox = await card.BoundingBoxAsync()
            ?? throw new InvalidOperationException($"Could not locate a bounding box for card '{candidateNameFragment}'.");

        // WaitForLoadedAsync only confirms the Kanban widget shell itself exists, not that all of
        // its (up to six) column headers have individually finished rendering yet (same race
        // HasColumnHeaderAsync above already guards against) — a bare instant CountAsync() here can
        // run before any header has rendered and see zero. Wait for the target column's own header
        // specifically before counting/indexing into the header row.
        if (!await HasColumnHeaderAsync(targetStageName))
            throw new InvalidOperationException($"Could not find a Kanban column header for stage '{targetStageName}'.");

        var targetIndex = await GetColumnIndexAsync(targetStageName);

        var contentCells = Board.Locator(".e-content-cells");
        var targetCell = contentCells.Nth(targetIndex);
        await targetCell.ScrollIntoViewIfNeededAsync();

        var startX = cardBox.X + cardBox.Width / 2;
        var startY = cardBox.Y + cardBox.Height / 2;

        await page.Mouse.MoveAsync(startX, startY);
        await page.Mouse.DownAsync();

        // A small "wiggle" right after mousedown, well short of leaving the card, so Syncfusion's
        // own drag-start threshold is crossed and its drag-start reflow (removing the card from
        // its origin column, inserting a placeholder) has already happened before any of the real
        // movement below — otherwise that reflow can shift column layout mid-drag, making a
        // bounding box captured before drag-start stale by the time of the drop (observed
        // symptom: the drop landing right on the boundary between two columns instead of solidly
        // inside the target one, which Syncfusion then can't resolve to a column at all and just
        // reverts the card to its original position).
        await page.Mouse.MoveAsync(startX + 5, startY + 5);
        await page.WaitForTimeoutAsync(100);

        // Re-measure the target column now that the drag (and its reflow) is actually in progress,
        // rather than trusting the pre-drag snapshot. Aim at 35% across the column's width — closer
        // to its center-left than dead center — so a few more pixels of measurement drift still
        // land solidly inside the column instead of drifting into the boundary with its neighbor.
        var liveTargetBox = await targetCell.BoundingBoxAsync()
            ?? throw new InvalidOperationException($"Could not locate a bounding box for the '{targetStageName}' column's content cell.");
        var endX = liveTargetBox.X + liveTargetBox.Width * 0.35f;
        var endY = liveTargetBox.Y + Math.Min(liveTargetBox.Height / 2, 40f);

        const int steps = 12;
        for (var i = 1; i <= steps; i++)
        {
            var x = startX + (endX - startX) * i / steps;
            var y = startY + (endY - startY) * i / steps;
            await page.Mouse.MoveAsync(x, y);
            await page.WaitForTimeoutAsync(50);
        }

        // Dwell briefly at the drop point before releasing — Syncfusion's drop-zone highlight
        // (".e-dropping" on the target ".e-content-cells") needs the pointer to sit still over it
        // for a moment to fully commit as the resolved drop target, not just pass through.
        await page.Mouse.MoveAsync(endX, endY);
        await page.WaitForTimeoutAsync(300);
        await page.Mouse.UpAsync();

        // DragStop's handler always re-fetches the board afterward (see VacancyKanbanBoard.razor's
        // OnDragStopAsync comment), whether the move was accepted or reverted — give that round-trip
        // a moment to settle before the caller asserts on the resulting column membership.
        await page.WaitForTimeoutAsync(500);
    }

    // ── Error banner ──────────────────────────────────────────────────────────────

    public Task<bool> IsErrorVisibleAsync() => Board.Locator("[data-testid='kanban-error']").IsVisibleAsync();

    public async Task<string?> GetErrorTextAsync()
    {
        var error = Board.Locator("[data-testid='kanban-error']");
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }
}
