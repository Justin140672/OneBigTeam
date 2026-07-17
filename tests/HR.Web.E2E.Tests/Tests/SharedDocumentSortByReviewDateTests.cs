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
/// (see ListSharedCompanyDocumentsHandler) — i.e. most-recently-created first. Each test below
/// scopes the visible dataset with the existing "Next Review Date From"/"To" filters (same
/// interaction pattern as the Category/Review Status filters — see
/// SharedDocumentReviewStatusFilterTests) to a review-date window (today+140..today+190) that no
/// other test in this suite uses (the widest offset used elsewhere is AddYears(1)/AddDays(60)),
/// to minimize interference from other seeded/persisted documents. Because the backend database
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
[Collection("E2E")]
public sealed class SharedDocumentSortByReviewDateTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    // A review-date window not used by any other AddDays/AddYears offset elsewhere in this test
    // suite (max seen elsewhere is AddDays(60) / AddYears(1)), to keep unrelated seeded/persisted
    // documents out of the filtered view as much as possible.
    private static readonly DateOnly EarlyReviewDate = DateOnly.FromDateTime(DateTime.Today.AddDays(150));
    private static readonly DateOnly MiddleReviewDate = DateOnly.FromDateTime(DateTime.Today.AddDays(165));
    private static readonly DateOnly LateReviewDate = DateOnly.FromDateTime(DateTime.Today.AddDays(180));
    private static readonly DateOnly FilterFrom = DateOnly.FromDateTime(DateTime.Today.AddDays(140));
    private static readonly DateOnly FilterTo = DateOnly.FromDateTime(DateTime.Today.AddDays(190));

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
            await ApplyReviewDateRangeFilterAsync(FilterFrom, FilterTo);

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
            await ApplyReviewDateRangeFilterAsync(FilterFrom, FilterTo);

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
            await ApplyReviewDateRangeFilterAsync(FilterFrom, FilterTo);

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

    // Narrows the visible grid to the "Next Review Date From"/"To" window using the plain
    // (non-dialog) SfDatePicker filters already on SharedDocuments.razor's filter toolbar — same
    // ".e-date-wrapper input.e-input" fill pattern as the upload dialog's "Next Review Date"
    // field, scoped to each filter's own ".col-md-2" label group so "From" and "To" (whose label
    // text doesn't overlap) resolve unambiguously.
    private async Task ApplyReviewDateRangeFilterAsync(DateOnly from, DateOnly to)
    {
        var fromInput = _page.Locator(".col-md-2")
            .Filter(new() { HasText = "Next Review Date From" })
            .Locator(".e-date-wrapper input.e-input");
        await fromInput.ClickAsync();
        await fromInput.FillAsync(from.ToString("dd/MM/yyyy"));
        await _page.Keyboard.PressAsync("Tab");
        await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 15_000 });

        var toInput = _page.Locator(".col-md-2")
            .Filter(new() { HasText = "Next Review Date To" })
            .Locator(".e-date-wrapper input.e-input");
        await toInput.ClickAsync();
        await toInput.FillAsync(to.ToString("dd/MM/yyyy"));
        await _page.Keyboard.PressAsync("Tab");
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
    private async Task<List<string>> GetRelativeOrderOfTitlesAsync(IReadOnlyCollection<string> titles)
    {
        var rows = _page.Locator(".e-row");
        var count = await rows.CountAsync();
        var order = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var cellText = (await rows.Nth(i).Locator(".e-rowcell").First.InnerTextAsync()).Trim();
            if (titles.Contains(cellText))
                order.Add(cellText);
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
    // SharedDocumentReviewStatusFilterTests.UploadDocumentWithReviewDateAsync /
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
        await categoryGroup.Locator("span[role='combobox']").First.ClickAsync();
        await _page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await _page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = "Policy" })
            .First
            .ClickAsync();

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
