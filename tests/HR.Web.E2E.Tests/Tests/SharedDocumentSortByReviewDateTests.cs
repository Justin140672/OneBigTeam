using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers "Sort Documents by Next Review Date" on the Shared Documents list
/// (SharedDocuments.razor): the "Next Review Date" column has no
/// <c>AllowSorting="false"</c> override, so Syncfusion's native 3-state header-click sort
/// (ascending -&gt; descending -&gt; none/original) already works client-side against the
/// full in-memory <c>_documents</c> list rendered by
/// <c>&lt;HrGrid TValue="SharedCompanyDocumentListItem" ... AllowSorting="true"&gt;</c>. No grid,
/// Handler, or DTO changes were needed for this story — these tests exist purely to prove the
/// existing behaviour end-to-end.
///
/// Column order in SharedDocuments.razor's GridColumns (0-based, matches DOM order of
/// ".e-rowcell" per row): 0=Title, 1=Category, 2=Version, 3=Status, 4=Effective Date,
/// 5=Next Review Date, 6=Review Frequency, 7=Review Owner, 8=Last Updated, 9=Updated By.
///
/// The list endpoint's default (unsorted) order is <c>OrderByDescending(d =&gt; d.CreatedAt)</c>
/// (see ListSharedCompanyDocumentsHandler) — i.e. most-recently-created first. The list filter bar
/// (Search/Status/Review Status/Category/Next Review Date range) has been removed, so these tests
/// no longer scope the visible dataset with a review-date range filter. Because the backend database
/// is shared across the whole "E2E" collection (DisableParallelization = true, so tests run
/// strictly sequentially — see E2ECollection) and the date window alone can't
/// guarantee only this test's rows are visible, row-order assertions never assume the grid
/// contains *only* the documents this test created: they read the full list of visible ".e-row"
/// titles and extract just the relative order of this test's own distinctively-titled
/// (<c>$"Sort Test {Guid.NewGuid():N}"</c>) documents among them.
///
/// Header click target: Syncfusion's EJ2 Grid (which HrGrid/SfGrid renders under the hood) marks
/// each header cell as ".e-headercell" containing a ".e-headercelldiv" — clicking that div is the
/// standard way to trigger a single-column sort/cycle. No prior E2E test in this suite already
/// exercises grid header sorting, so there's no existing helper/prior-art class name to reuse for
/// waiting on the sort-indicator; ".e-headercell.e-ascending" / ".e-headercell.e-descending" are
/// EJ2's documented convention for the sorted-header CSS classes, used here as a best-effort wait
/// with a short fallback settle delay if that assumption doesn't hold — the row-order assertions
/// that follow are the actual source of truth for each test, independent of whether the indicator
/// wait succeeds.
/// </summary>
public sealed class SharedDocumentSortByReviewDateTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    // A review-date window not used by any other AddDays/AddYears offset elsewhere in this test
    // suite (max seen elsewhere is AddDays(60) / AddYears(1)), to keep unrelated seeded/persisted
    // documents out of the filtered view as much as possible.
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

            // First click -> ascending, second click -> descending (Syncfusion's native 3-click
            // single-column sort cycle: ascending -> descending -> none/original).
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

            // Third click on a single (non-multi) sort column removes sorting entirely and
            // restores the grid's original, pre-sort row order.
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

    // Third click of the cycle removes sorting rather than adding a new direction class, so there
    // is no "e-ascending"/"e-descending" class to positively wait for here — best-effort wait for
    // both to be gone, falling back to a short settle delay.
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

        // Bounded by wall-clock time rather than a fixed page count — the shared, long-lived E2E
        // dev database's document count keeps growing as more of the suite runs over time (a fixed
        // 50-page/1000-row cap here was itself observed going stale as that happened, the exact
        // same class of "assumed-generous constant becomes insufficient later" issue this whole
        // page-walk replaced a fixed page-1-only read for in the first place). 60s comfortably
        // covers many hundreds of pages at ~200ms/page even if the dataset keeps growing further.
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (remaining.Count > 0 && DateTime.UtcNow < deadline)
        {
            var rows = _page.Locator(".e-row");
            var count = await rows.CountAsync();
            for (var i = 0; i < count; i++)
            {
                var cellText = (await rows.Nth(i).Locator(".e-rowcell").First.InnerTextAsync()).Trim();
                if (remaining.Remove(cellText))
                    order.Add(cellText);
            }

            if (remaining.Count == 0) break;

            var nextButton = _page.Locator(".e-pagenextdiv, .e-nextpage").First;
            if (await nextButton.CountAsync() == 0) break;
            var isDisabled = (await nextButton.GetAttributeAsync("class"))?.Contains("e-disable") == true
                || (await nextButton.GetAttributeAsync("aria-disabled")) == "true";
            if (isDisabled) break;

            await nextButton.ClickAsync();
            await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 15_000 });
            // Give the page's own re-render a moment to settle before reading — same
            // "container before content" race fixed elsewhere in this suite for grid reloads.
            await _page.WaitForTimeoutAsync(200);
        }

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

    // Uploads a shared document from the Shared Documents list page with a Next Review Date —
    // same upload-dialog interaction pattern as
    // HrDashboardTests.UploadDocumentWithReviewDateAsync / SharedDocumentListReviewColumnsTests.
    // UploadDocumentAsync.
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
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
    }

    // %PDF- followed by padding, so magic-byte content validation passes.
    private static byte[] BuildTestPdf()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }
}
