using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class VacancyKanbanBoardPage(IPage page, string baseUrl)
{
    private ILocator Board => page.Locator("[data-testid='vacancy-kanban-board']");

    public async Task GoToStandaloneAsync(Guid companyId, Guid vacancyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/vacancies/{vacancyId}/kanban");
        await WaitForLoadedAsync();
    }

    public async Task WaitForLoadedAsync()
    {
        await page.WaitForSelectorAsync("[data-testid='vacancy-kanban-board']", new() { Timeout = 20_000 });
        await page.WaitForSelectorAsync(
            "[data-testid='vacancy-kanban-board'] [data-testid='kanban-columns']",
            new() { Timeout = 20_000 });
    }


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
        var input = page.Locator("input[aria-label='Search pipeline candidates'], input[data-testid='kanban-search-box'], [data-testid='kanban-search-box'] input").First;
        await input.FillAsync(text);
        await input.PressAsync("Tab");

        await Board.Page.WaitForTimeoutAsync(400);
    }

    public async Task<int> CountVisibleCardsAsync() =>
        await Board.Locator(".kanban-candidate-card").CountAsync();

    public Task WaitForCardPresentAsync(string candidateNameFragment) =>
        Assertions.Expect(Board.Locator(".kanban-candidate-card").Filter(new() { HasText = candidateNameFragment }))
            .Not.ToHaveCountAsync(0, new() { Timeout = 15_000 });

    public Task WaitForCardAbsentAsync(string candidateNameFragment) =>
        Assertions.Expect(Board.Locator(".kanban-candidate-card").Filter(new() { HasText = candidateNameFragment }))
            .ToHaveCountAsync(0, new() { Timeout = 15_000 });

    public async Task<bool> HasCardForNameAsync(string candidateNameFragment) =>
        await Board.Locator(".kanban-candidate-card").Filter(new() { HasText = candidateNameFragment }).CountAsync() > 0;


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

    private async Task<ILocator> ColumnAsync(string stageName)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var column = await TryColumnAsync(stageName);
            if (column is not null) return column;

            if (DateTime.UtcNow >= deadline)
                throw new InvalidOperationException($"Could not find a Kanban column for stage '{stageName}'.");

            await page.WaitForTimeoutAsync(200);
        }
    }

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

    public async Task<int> GetColumnCountAsync(string stageName)
    {
        var column = await ColumnAsync(stageName);
        var text = (await column.Locator(".vacancy-kanban-board__column-count").TextContentAsync()) ?? "";
        return int.TryParse(text.Trim(), out var parsed) ? parsed : 0;
    }

    public async Task<bool> IsCardInColumnAsync(string candidateNameFragment, string stageName)
    {
        var column = await ColumnAsync(stageName);
        var cardInColumn = column.Locator(".kanban-candidate-card").Filter(new() { HasText = candidateNameFragment });

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            if (await cardInColumn.CountAsync() > 0)
                return true;

            if (DateTime.UtcNow >= deadline)
                return false;

            await page.WaitForTimeoutAsync(200);
        }
    }


    private ILocator Card(string candidateNameFragment) =>
        Board.Locator(".kanban-candidate-card").Filter(new() { HasText = candidateNameFragment }).First;

    public Task<bool> IsCardVisibleAsync(string candidateNameFragment) => Card(candidateNameFragment).IsVisibleAsync();

    public async Task<string> GetCardClassAsync(string candidateNameFragment) =>
        (await Card(candidateNameFragment).GetAttributeAsync("class")) ?? "";

    public Task<bool> HasRejectedStylingAsync(string candidateNameFragment) =>
        HasClassAsync(candidateNameFragment, "kanban-candidate-card--danger");

    public Task<bool> HasHiredStylingAsync(string candidateNameFragment) =>
        HasClassAsync(candidateNameFragment, "kanban-candidate-card--success");

    private async Task<bool> HasClassAsync(string candidateNameFragment, string cssClass) =>
        (await GetCardClassAsync(candidateNameFragment)).Split(' ').Contains(cssClass);

    public async Task<string?> GetCardStatusBadgeTextAsync(string candidateNameFragment)
    {
        var badge = Card(candidateNameFragment).Locator("[data-testid='kanban-card-terminal-status'] .badge");
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    public async Task<string?> GetCardRecruiterTextAsync(string candidateNameFragment)
    {
        var meta = Card(candidateNameFragment).Locator(".kanban-candidate-card__meta");
        return (await meta.TextContentAsync())?.Trim();
    }


    public Task ClickCardAsync(string candidateNameFragment) => Card(candidateNameFragment).ClickAsync();

    public async Task DragCardToColumnAsync(string candidateNameFragment, string targetStageName)
    {
        var card = Card(candidateNameFragment);
        var column = await ColumnAsync(targetStageName);
        var targetColumn = column.Locator(".vacancy-kanban-board__column-content");

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await DispatchDragSequenceAsync(card, targetColumn);

            // The drop handler (OnDropAsync) always re-fetches the board afterward, whether the move
            // was accepted or rejected by the server — give that round-trip a moment to settle
            // before checking (or before the caller's own subsequent assertion, on the final/only
            // attempt).
            await page.WaitForTimeoutAsync(500);

            if (attempt == 3 || await IsCardInColumnAsync(candidateNameFragment, targetStageName))
                return;

            card = Card(candidateNameFragment);
            column = await ColumnAsync(targetStageName);
            targetColumn = column.Locator(".vacancy-kanban-board__column-content");
        }
    }

    /// <summary>
    /// Dispatches the dragstart → dragenter → dragover → drop → dragend DOM event sequence directly
    /// against <paramref name="source"/> and <paramref name="target"/>, without relying on the
    /// browser's native HTML5 drag gesture or Playwright's own mouse-based drag simulation. See
    /// DragCardToColumnAsync's remarks for why: this app's own OnDropAsync/@ondrop:preventDefault
    /// wiring only cares that the events fire, not what (if anything) is on the DataTransfer.
    /// </summary>
    private static async Task DispatchDragSequenceAsync(ILocator source, ILocator target)
    {
        await source.EvaluateAsync(@"el => {
            const dt = new DataTransfer();
            el.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer: dt }));
        }");

        await target.EvaluateAsync(@"el => {
            const dt = new DataTransfer();
            el.dispatchEvent(new DragEvent('dragenter', { bubbles: true, cancelable: true, dataTransfer: dt }));
            el.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: dt }));
            el.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));
        }");

        await source.EvaluateAsync(@"el => {
            const dt = new DataTransfer();
            el.dispatchEvent(new DragEvent('dragend', { bubbles: true, cancelable: true, dataTransfer: dt }));
        }");
    }


    public async Task<bool> IsErrorVisibleAsync()
    {
        var error = Board.Locator("[data-testid='kanban-error']");
        // An instant, non-retrying IsVisibleAsync() right after a rejected move's fixed settle
        // delay can still race the board's re-render over SignalR and read false before the error
        // banner has actually appeared. Poll briefly instead of a single snapshot (same rationale
        // as ColumnAsync/IsCardInColumnAsync above).
        try
        {
            await error.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string?> GetErrorTextAsync()
    {
        var error = Board.Locator("[data-testid='kanban-error']");
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }


    public async Task<string?> GetColumnEmptyTextAsync(string stageName)
    {
        var column = await ColumnAsync(stageName);
        var empty = column.Locator(".vacancy-kanban-board__column-empty");
        return await empty.IsVisibleAsync() ? (await empty.TextContentAsync())?.Trim() : null;
    }


    private ILocator ColumnsContainer => Board.Locator("[data-testid='kanban-columns']");

    public async Task<bool> HasDraggingClassAsync() =>
        ((await ColumnsContainer.GetAttributeAsync("class")) ?? "").Split(' ').Contains("dragging");

    public async Task<bool> ObserveDraggingClassDuringManualDragAsync(string candidateNameFragment)
    {
        var card = Card(candidateNameFragment);
        await card.ScrollIntoViewIfNeededAsync();

        var duringDrag = false;
        for (var attempt = 1; attempt <= 4 && !duringDrag; attempt++)
        {
            await card.EvaluateAsync(@"el => {
                const dt = new DataTransfer();
                el.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer: dt }));
            }");

            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < deadline)
            {
                if (await HasDraggingClassAsync())
                {
                    duringDrag = true;
                    break;
                }
                await card.Page.WaitForTimeoutAsync(100);
            }

            if (!duringDrag)
            {
                await card.EvaluateAsync(@"el => {
                    const dt = new DataTransfer();
                    el.dispatchEvent(new DragEvent('dragend', { bubbles: true, cancelable: true, dataTransfer: dt }));
                }");
            }
        }

        await card.EvaluateAsync(@"el => {
            const dt = new DataTransfer();
            el.dispatchEvent(new DragEvent('dragend', { bubbles: true, cancelable: true, dataTransfer: dt }));
        }");

        var endDeadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < endDeadline && await HasDraggingClassAsync())
            await card.Page.WaitForTimeoutAsync(100);

        return duringDrag;
    }


    private ILocator MoveStageButton(string candidateNameFragment) =>
        Card(candidateNameFragment).Locator("[data-testid='kanban-card-move-stage-btn']");

    private ILocator MoveStageMenu(string candidateNameFragment) =>
        Card(candidateNameFragment).Locator("[data-testid='kanban-card-move-stage-menu']");

    private ILocator MoveStageMenuItem(string candidateNameFragment, string targetStageName) =>
        MoveStageMenu(candidateNameFragment).GetByRole(AriaRole.Menuitem, new() { Name = targetStageName, Exact = true });

    public async Task MoveToStageViaMouseAsync(string candidateNameFragment, string targetStageName)
    {
        await MoveStageButton(candidateNameFragment).ClickAsync();
        await MoveStageMenu(candidateNameFragment).WaitForAsync(new() { Timeout = 10_000 });
        await MoveStageMenuItem(candidateNameFragment, targetStageName).ClickAsync();

        await page.WaitForTimeoutAsync(500);
    }

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

    public async Task<ILocator> OpenCardMenuAsync(string candidateNameFragment)
    {
        var menu = MoveStageMenu(candidateNameFragment);
        for (var attempt = 1; ; attempt++)
        {
            if (!await menu.IsVisibleAsync())
                await MoveStageButton(candidateNameFragment).ClickAsync();

            try
            {
                await menu.WaitForAsync(new() { Timeout = attempt < 4 ? 4_000 : 15_000 });
                return menu;
            }
            catch (TimeoutException) when (attempt < 4)
            {
            }
        }
    }

    public async Task ExpectCardMenuItemAsync(string candidateNameFragment, string testId)
    {
        var menu = await OpenCardMenuAsync(candidateNameFragment);
        await menu.Locator($"[data-testid='{testId}']").WaitForAsync(new() { Timeout = 10_000 });
    }

    public async Task ClickReviewCvFromCardMenuAsync(string candidateNameFragment)
    {
        for (var attempt = 1; ; attempt++)
        {
            var menu = await OpenCardMenuAsync(candidateNameFragment);
            await menu.Locator("[data-testid='kanban-card-review-cv']").ClickAsync();

            try
            {
                await page.WaitForURLAsync("**/review-cv**",
                    new() { Timeout = attempt < 3 ? 8_000 : 30_000, WaitUntil = WaitUntilState.Commit });
                return;
            }
            catch (TimeoutException) when (attempt < 3)
            {
            }
        }
    }

    // ── Internal recruitment Ticket 6: Internal badge on the card ────────────────
    // Cards are addressed by their stable data-application-id (KanbanCandidateCard.razor), scoped to
    // the kanban-candidate-card testid — never by the badge's CSS class alone. Columns carry
    // data-stage-id (VacancyKanbanBoard.razor), which identifies a stage without relying on its name.

    private static string CardSelector(Guid applicationId) =>
        $".kanban-candidate-card[data-testid='kanban-candidate-card'][data-application-id='{applicationId}']";

    private ILocator CardByApplicationId(Guid applicationId) => Board.Locator(CardSelector(applicationId));

    public async Task ExpectCardInternalBadgeAsync(Guid applicationId, bool isInternal)
    {
        var card = CardByApplicationId(applicationId);
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var badge = card.Locator("[data-testid='internal-application-badge']");
        if (isInternal)
        {
            await Assertions.Expect(badge).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Assertions.Expect(badge).ToHaveTextAsync("Internal");
        }
        else
        {
            await Assertions.Expect(badge).ToHaveCountAsync(0);
        }
    }

    public async Task<(string StageId, string Title)> GetColumnOfCardAsync(Guid applicationId)
    {
        var column = Board.Locator($"[data-testid='kanban-column']:has({CardSelector(applicationId)})");
        await Assertions.Expect(column).ToHaveCountAsync(1, new() { Timeout = 30_000 });
        var stageId = await column.GetAttributeAsync("data-stage-id") ?? "";
        var title = ((await column.Locator(".vacancy-kanban-board__column-title").TextContentAsync()) ?? "").Trim();
        return (stageId, title);
    }

    public Task ExpectCardInStageColumnAsync(Guid applicationId, string stageId) =>
        Assertions.Expect(
                Board.Locator($"[data-testid='kanban-column'][data-stage-id='{stageId}']")
                    .Locator(CardSelector(applicationId)))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
}
