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
/// Column/card structure assumptions (Syncfusion Blazor Kanban, SfKanban/KanbanColumn/KanbanCardSettings):
/// the widget renders a header row with one ".e-header-cell" per KanbanColumn (containing
/// ".e-header-text" for the HeaderText and ".e-item-count" for the ShowItemCount badge), and a
/// content row with one ".e-content-cell" per column holding its cards. Card content itself is our
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
            "[data-testid='vacancy-kanban-board'] .e-kanban, [data-testid='vacancy-kanban-board'] .e-content-cell",
            new() { Timeout = 20_000 });
    }

    // ── Search box (ticket #69) ──────────────────────────────────────────────────

    // Tolerant of either DOM shape Syncfusion's SfTextBox might render the data-testid onto: the
    // attribute could land directly on the <input> itself, or on a wrapper (e.g. ".e-input-group")
    // containing a nested <input> — this was never confirmed against a live board before this file
    // was written, so match both rather than assuming one.
    public Task FillSearchAsync(string text) =>
        Board.Locator("input[data-testid='kanban-search-box'], [data-testid='kanban-search-box'] input").First.FillAsync(text);

    public async Task<int> CountVisibleCardsAsync() =>
        await Board.Locator(".kanban-applicant-card").CountAsync();

    public async Task<bool> HasCardForNameAsync(string candidateNameFragment) =>
        await Board.Locator(".kanban-applicant-card").Filter(new() { HasText = candidateNameFragment }).CountAsync() > 0;

    // ── Columns (tickets #69/#71) ────────────────────────────────────────────────

    private ILocator HeaderCell(string stageName) =>
        Board.Locator(".e-header-cell").Filter(new() { HasText = stageName }).First;

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
    /// <paramref name="stageName"/>. Falls back to parsing any trailing digits out of the header
    /// cell's full text if the dedicated count element isn't present under that exact class name in
    /// the installed Syncfusion version — kept deliberately tolerant since this couldn't be
    /// confirmed against a running board.
    /// </summary>
    public async Task<int> GetColumnCountAsync(string stageName)
    {
        var header = HeaderCell(stageName);
        var countEl = header.Locator(".e-item-count");
        if (await countEl.CountAsync() > 0)
        {
            var text = (await countEl.First.TextContentAsync())?.Trim() ?? "";
            if (int.TryParse(text, out var parsed)) return parsed;
        }

        var full = (await header.TextContentAsync()) ?? "";
        var digits = new string(full.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var fallback) ? fallback : 0;
    }

    // ── Card content / visual distinction (ticket #71) ───────────────────────────

    private ILocator Card(string candidateNameFragment) =>
        Board.Locator(".kanban-applicant-card").Filter(new() { HasText = candidateNameFragment }).First;

    public Task<bool> IsCardVisibleAsync(string candidateNameFragment) => Card(candidateNameFragment).IsVisibleAsync();

    public async Task<string> GetCardClassAsync(string candidateNameFragment) =>
        (await Card(candidateNameFragment).GetAttributeAsync("class")) ?? "";

    public Task<bool> HasWithdrawnStylingAsync(string candidateNameFragment) =>
        HasClassAsync(candidateNameFragment, "kanban-applicant-card--muted");

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
    // Best-effort: Syncfusion's EJ2 Kanban (which SfKanban wraps) implements its own pointer-based
    // drag rather than native HTML5 drag-and-drop, so a plain Locator.DragToAsync (which dispatches
    // dragstart/drop DOM events) is unlikely to be recognized by the widget. A manual mouse
    // down/move/up sequence — hovering the source card, pressing, moving in a few incremental steps
    // toward the target column's content area, then releasing — is the commonly-used workaround for
    // EJ2 widgets in Playwright and is what's used here, but this has NOT been run/verified against
    // a live board; timing (steps/delay) may need tuning once it's actually exercised.
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

        // The target column's content cell is identified by column position relative to its header
        // — the header row and content row share the same column order (ApplicationStatusTransitionRules.
        // ColumnOrder), so the header cell's index is used to find the matching content cell.
        var headerCells = Board.Locator(".e-header-cell");
        var headerCount = await headerCells.CountAsync();
        int targetIndex = -1;
        for (var i = 0; i < headerCount; i++)
        {
            var text = (await headerCells.Nth(i).TextContentAsync()) ?? "";
            if (text.Contains(targetStageName, StringComparison.OrdinalIgnoreCase))
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex < 0)
            throw new InvalidOperationException($"Could not find a Kanban column header for stage '{targetStageName}'.");

        var contentCells = Board.Locator(".e-content-cell");
        var targetCell = contentCells.Nth(targetIndex);
        await targetCell.ScrollIntoViewIfNeededAsync();
        var targetBox = await targetCell.BoundingBoxAsync()
            ?? throw new InvalidOperationException($"Could not locate a bounding box for the '{targetStageName}' column's content cell.");

        var startX = cardBox.X + cardBox.Width / 2;
        var startY = cardBox.Y + cardBox.Height / 2;
        var endX = targetBox.X + targetBox.Width / 2;
        var endY = targetBox.Y + Math.Min(targetBox.Height / 2, 40);

        await page.Mouse.MoveAsync(startX, startY);
        await page.Mouse.DownAsync();

        const int steps = 8;
        for (var i = 1; i <= steps; i++)
        {
            var x = startX + (endX - startX) * i / steps;
            var y = startY + (endY - startY) * i / steps;
            await page.Mouse.MoveAsync(x, y);
            await page.WaitForTimeoutAsync(50);
        }

        await page.Mouse.MoveAsync(endX, endY);
        await page.WaitForTimeoutAsync(100);
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
