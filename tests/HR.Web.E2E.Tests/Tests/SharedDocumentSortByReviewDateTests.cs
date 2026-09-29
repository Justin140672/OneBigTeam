using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class SharedDocumentSortByReviewDateTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    private static readonly DateOnly EarlyReviewDate = DateOnly.FromDateTime(DateTime.Today.AddDays(150));
    private static readonly DateOnly MiddleReviewDate = DateOnly.FromDateTime(DateTime.Today.AddDays(165));
    private static readonly DateOnly LateReviewDate = DateOnly.FromDateTime(DateTime.Today.AddDays(180));

    [Fact]
    public async Task ReviewDateHeader_ClickedOnce_SortsRowsByReviewDateAscending()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var earlyTitle = $"Sort Test {Guid.NewGuid():N}";
        var middleTitle = $"Sort Test {Guid.NewGuid():N}";
        var lateTitle = $"Sort Test {Guid.NewGuid():N}";
        var files = new List<string>();
        try
        {
            await UploadDocumentWithReviewDateAsync(earlyTitle, NewTempFile(files), EarlyReviewDate);
            await UploadDocumentWithReviewDateAsync(middleTitle, NewTempFile(files), MiddleReviewDate);
            await UploadDocumentWithReviewDateAsync(lateTitle, NewTempFile(files), LateReviewDate);

            await GoToListPageAsync();

            await ClickReviewDateHeaderAsync(expectedDirectionClass: "e-ascending");

            var ourTitles = new[] { earlyTitle, middleTitle, lateTitle };
            var order = await GetRelativeOrderOfTitlesAsync(ourTitles);

            Assert.Equal([earlyTitle, middleTitle, lateTitle], order);
        }
        finally
        {
            DeleteFiles(files);
        }
    }

    [Fact]
    public async Task ReviewDateHeader_ClickedTwice_SortsRowsByReviewDateDescending()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var earlyTitle = $"Sort Test {Guid.NewGuid():N}";
        var middleTitle = $"Sort Test {Guid.NewGuid():N}";
        var lateTitle = $"Sort Test {Guid.NewGuid():N}";
        var files = new List<string>();
        try
        {
            await UploadDocumentWithReviewDateAsync(earlyTitle, NewTempFile(files), EarlyReviewDate);
            await UploadDocumentWithReviewDateAsync(middleTitle, NewTempFile(files), MiddleReviewDate);
            await UploadDocumentWithReviewDateAsync(lateTitle, NewTempFile(files), LateReviewDate);

            await GoToListPageAsync();

            await ClickReviewDateHeaderAsync(expectedDirectionClass: "e-ascending");
            await ClickReviewDateHeaderAsync(expectedDirectionClass: "e-descending");

            var ourTitles = new[] { earlyTitle, middleTitle, lateTitle };
            var order = await GetRelativeOrderOfTitlesAsync(ourTitles);

            Assert.Equal([lateTitle, middleTitle, earlyTitle], order);
        }
        finally
        {
            DeleteFiles(files);
        }
    }

    [Fact]
    public async Task ReviewDateHeader_ClickedThreeTimes_RestoresOriginalDefaultOrder()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var earlyTitle = $"Sort Test {Guid.NewGuid():N}";
        var middleTitle = $"Sort Test {Guid.NewGuid():N}";
        var lateTitle = $"Sort Test {Guid.NewGuid():N}";
        var files = new List<string>();
        try
        {
            await UploadDocumentWithReviewDateAsync(earlyTitle, NewTempFile(files), EarlyReviewDate);
            await UploadDocumentWithReviewDateAsync(middleTitle, NewTempFile(files), MiddleReviewDate);
            await UploadDocumentWithReviewDateAsync(lateTitle, NewTempFile(files), LateReviewDate);

            await GoToListPageAsync();

            var ourTitles = new[] { earlyTitle, middleTitle, lateTitle };

            // Captured live from the page's initial/default load — deliberately NOT assumed to be
            // any particular order (e.g. upload order or CreatedAt order): sorting is proven to be
            // a distinct, opt-in action purely by comparing against whatever this actually is.
            var defaultOrder = await GetRelativeOrderOfTitlesAsync(ourTitles);

            await ClickReviewDateHeaderAsync(expectedDirectionClass: "e-ascending");
            var ascendingOrder = await GetRelativeOrderOfTitlesAsync(ourTitles);
            Assert.Equal([earlyTitle, middleTitle, lateTitle], ascendingOrder);
            Assert.NotEqual(defaultOrder, ascendingOrder);

            await ClickReviewDateHeaderAsync(expectedDirectionClass: "e-descending");
            var descendingOrder = await GetRelativeOrderOfTitlesAsync(ourTitles);
            Assert.Equal([lateTitle, middleTitle, earlyTitle], descendingOrder);

            await ClickReviewDateHeaderAndWaitForUnsortedAsync();
            var restoredOrder = await GetRelativeOrderOfTitlesAsync(ourTitles);
            Assert.Equal(defaultOrder, restoredOrder);
        }
        finally
        {
            DeleteFiles(files);
        }
    }

    private ILocator ReviewDateHeaderCell =>
        _page.Locator(".e-headercell").Filter(new() { HasText = "Next Review Date" });

    private async Task GoToListPageAsync()
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });
        await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 15_000 });
    }

    // Clicks the "Next Review Date" column's ".e-headercelldiv" (the standard EJ2 Grid
    // single-column-sort click target) and best-effort waits for the sort-indicator class
    // Syncfusion is documented to apply to the sorted ".e-headercell" ("e-ascending" /
    // "e-descending"). Falls back to a short settle delay if that class assumption doesn't hold —
    // callers assert on actual row order afterwards regardless, so this wait only exists to avoid
    // reading row order mid-transition.
    private async Task ClickReviewDateHeaderAsync(string expectedDirectionClass)
    {
        await ReviewDateHeaderCell.Locator(".e-headercelldiv").First.ClickAsync();
        try
        {
            await _page.WaitForSelectorAsync(
                $".e-headercell.{expectedDirectionClass}:has-text('Next Review Date')",
                new() { Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            await _page.WaitForTimeoutAsync(500);
        }
    }

    private async Task ClickReviewDateHeaderAndWaitForUnsortedAsync()
    {
        await ReviewDateHeaderCell.Locator(".e-headercelldiv").First.ClickAsync();
        try
        {
            await _page.WaitForSelectorAsync(
                ".e-headercell.e-ascending:has-text('Next Review Date'), .e-headercell.e-descending:has-text('Next Review Date')",
                new() { State = WaitForSelectorState.Detached, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            await _page.WaitForTimeoutAsync(500);
        }
    }

    // Reads every visible ".e-row" in DOM order (Title is column 0) and returns just the subset
    // (in the order encountered) whose Title matches one of `titles` — deliberately tolerant of
    // other seeded/persisted documents sharing the visible page, per the "relative order among
    // own tracked titles" strategy: the review-date range filter narrows the dataset but can't
    // guarantee it's the *only* thing on screen, since the backend database is shared and
    // persists across every test in the "E2E" collection.
    //
    // The grid paginates (AllowPaging="true", PageSize="20" — see SharedDocuments.razor) and this
    // page has no search/filter bar to narrow the dataset (removed in a later product change — see
    // class remarks). Our own 3 tracked documents use Next Review Date offsets of 150-180 days
    // specifically to stay out of most OTHER tests' review-date ranges, but as the shared,
    // long-lived E2E dev database accumulates more and more shared documents across the whole
    // suite over time, even that window can end up sorting well past page 1 (ascending sort in
    // particular: any of the many other documents with a nearer/overdue review date sorts ahead).
    // Reading only page 1 used to occasionally miss some of our 3 titles; it now reliably misses
    // ALL of them. Walk every page instead of just the first, so this stays correct regardless of
    // how large the shared dataset has grown — sort order is server-side/global, so collecting
    // matches page-by-page in pager order still preserves the overall relative ordering asserted
    // on by callers.
    private async Task<List<string>> GetRelativeOrderOfTitlesAsync(IReadOnlyCollection<string> titles)
    {
        var order = new List<string>();
        var remaining = new HashSet<string>(titles);

        await _page.VisitAllGridPagesAsync(async () =>
        {
            var firstCells = await _page.Locator(".e-grid .e-row > .e-rowcell:first-child").AllInnerTextsAsync();
            foreach (var text in firstCells.Select(t => t.Trim()))
            {
                if (remaining.Remove(text))
                    order.Add(text);
            }
            return remaining.Count == 0;
        }, TimeSpan.FromSeconds(60));

        return order;
    }

    private static string NewTempFile(List<string> tracked)
    {
        var path = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        tracked.Add(path);
        return path;
    }

    private static void DeleteFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private async Task UploadDocumentWithReviewDateAsync(string title, string filePath, DateOnly reviewDate)
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("Document title").FillAsync(title);

        var categoryGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await DropDownSelector.SelectAsync(_page, categoryGroup, "Policy");

        var reviewDateInput = dialog.Locator(".col-md-6")
            .Filter(new() { HasText = "Next Review Date" })
            .Locator(".e-date-wrapper input.e-input");
        await reviewDateInput.ClickAsync();
        await reviewDateInput.FillAsync(reviewDate.ToString("dd/MM/yyyy"));
        await _page.Keyboard.PressAsync("Tab");

        await File.WriteAllBytesAsync(filePath, BuildTestPdf());
        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 30_000 });

        await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
    }

    private static byte[] BuildTestPdf()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }
}
