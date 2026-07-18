using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "Review Status" filter dropdown added to SharedDocuments.razor's existing filter
/// toolbar (between "Status" and "Category"): its default placeholder state, and each of its four
/// buckets — "Due Soon", "Overdue", "No Review", "Expired" — filtering the grid down to only the
/// documents that fall into that bucket, ANDed with no other active filters here. Bucket
/// definitions (server-side, already tested — see the Documents module's handler tests) are:
/// Due Soon = non-archived/non-expired with Next Review Date in [today, today+7]; Overdue =
/// non-archived/non-expired with Next Review Date in the past; No Review = non-archived/non-expired
/// with no Next Review Date; Expired = Status is Expired regardless of Next Review Date.
///
/// Upload follows the same UI flow as SharedDocumentListReviewColumnsTests /
/// HrDashboardTests.UploadDocumentWithReviewDateAsync (past review dates are accepted by the
/// upload dialog's date picker — HrDashboardTests already relies on this for its "Overdue" widget
/// fixture). Expire follows SharedDocumentExpireTests.ExpireAsync via SharedDocumentDetailPage.
///
/// Uses Laura Bennett (laura.bennett@acme.example, HrAdministrator) against the seeded Acme
/// company, matching the other Shared Documents E2E tests.
/// </summary>
[Collection("E2E")]
public sealed class SharedDocumentReviewStatusFilterTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task ReviewStatusDropdown_OnLoad_ShowsPlaceholderAndAllDocuments()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentWithReviewDateAsync(title, tempFile, reviewDate: null);

            await GoToListPageAsync();

            // Syncfusion renders the placeholder via the underlying <input>'s native "placeholder"
            // attribute (browser-rendered ghost text), not as actual DOM text content — a
            // ToContainTextAsync check against the wrapping combobox span always sees "" here
            // regardless of what the placeholder says. Assert on the attribute directly instead.
            await Assertions.Expect(ReviewStatusCombobox.Locator("input"))
                .ToHaveAttributeAsync("placeholder", "All review statuses");
            Assert.True(await IsTitleVisibleInGridAsync(title));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReviewStatusFilter_DueSoon_ShowsOnlyDocumentsWithinSevenDayWindow()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var dueSoonTitle = $"Test Policy {Guid.NewGuid():N}";
        var dueSoonFile  = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        var farOutTitle  = $"Test Policy {Guid.NewGuid():N}";
        var farOutFile   = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        var noDateTitle  = $"Test Policy {Guid.NewGuid():N}";
        var noDateFile   = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            // Inside the "today + 7 days" window.
            await UploadDocumentWithReviewDateAsync(
                dueSoonTitle, dueSoonFile, DateOnly.FromDateTime(DateTime.Today.AddDays(3)));

            // Well outside the window — control that must NOT show up under "Due Soon".
            await UploadDocumentWithReviewDateAsync(
                farOutTitle, farOutFile, DateOnly.FromDateTime(DateTime.Today.AddDays(60)));

            // No review date at all — another control that must NOT show up under "Due Soon".
            await UploadDocumentWithReviewDateAsync(noDateTitle, noDateFile, reviewDate: null);

            await GoToListPageAsync();
            await SelectReviewStatusAsync("Due Soon");

            Assert.True(await IsTitleVisibleInGridAsync(dueSoonTitle),
                "Expected the near-term document to appear under the Due Soon filter");
            Assert.False(await IsTitleVisibleInGridAsync(farOutTitle),
                "Expected the far-future document to be excluded from the Due Soon filter");
            Assert.False(await IsTitleVisibleInGridAsync(noDateTitle),
                "Expected the no-review-date document to be excluded from the Due Soon filter");
        }
        finally
        {
            if (File.Exists(dueSoonFile)) File.Delete(dueSoonFile);
            if (File.Exists(farOutFile)) File.Delete(farOutFile);
            if (File.Exists(noDateFile)) File.Delete(noDateFile);
        }
    }

    [Fact]
    public async Task ReviewStatusFilter_Overdue_ShowsOnlyDocumentsWithPastReviewDate()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var overdueTitle = $"Test Policy {Guid.NewGuid():N}";
        var overdueFile  = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        var futureTitle  = $"Test Policy {Guid.NewGuid():N}";
        var futureFile   = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            // Next Review Date in the past — same pattern HrDashboardTests already relies on for
            // its "overdue" widget fixture (the upload dialog's date picker accepts past dates).
            await UploadDocumentWithReviewDateAsync(
                overdueTitle, overdueFile, DateOnly.FromDateTime(DateTime.Today.AddDays(-3)));

            // Future review date — control that must NOT show up under "Overdue".
            await UploadDocumentWithReviewDateAsync(
                futureTitle, futureFile, DateOnly.FromDateTime(DateTime.Today.AddDays(30)));

            await GoToListPageAsync();
            await SelectReviewStatusAsync("Overdue");

            Assert.True(await IsTitleVisibleInGridAsync(overdueTitle),
                "Expected the past-due document to appear under the Overdue filter");
            Assert.False(await IsTitleVisibleInGridAsync(futureTitle),
                "Expected the future-dated document to be excluded from the Overdue filter");
        }
        finally
        {
            if (File.Exists(overdueFile)) File.Delete(overdueFile);
            if (File.Exists(futureFile)) File.Delete(futureFile);
        }
    }

    [Fact]
    public async Task ReviewStatusFilter_NoReview_ShowsOnlyDocumentsWithoutAReviewDate()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var noDateTitle  = $"Test Policy {Guid.NewGuid():N}";
        var noDateFile   = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        var withDateTitle = $"Test Policy {Guid.NewGuid():N}";
        var withDateFile  = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentWithReviewDateAsync(noDateTitle, noDateFile, reviewDate: null);

            await UploadDocumentWithReviewDateAsync(
                withDateTitle, withDateFile, DateOnly.FromDateTime(DateTime.Today.AddDays(14)));

            await GoToListPageAsync();
            await SelectReviewStatusAsync("No Review");

            Assert.True(await IsTitleVisibleInGridAsync(noDateTitle),
                "Expected the document with no review date to appear under the No Review filter");
            Assert.False(await IsTitleVisibleInGridAsync(withDateTitle),
                "Expected the document with a review date to be excluded from the No Review filter");
        }
        finally
        {
            if (File.Exists(noDateFile)) File.Delete(noDateFile);
            if (File.Exists(withDateFile)) File.Delete(withDateFile);
        }
    }

    [Fact]
    public async Task ReviewStatusFilter_Expired_ShowsOnlyExpiredDocuments()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var expiredTitle = $"Test Policy {Guid.NewGuid():N}";
        var expiredFile  = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        var activeTitle  = $"Test Policy {Guid.NewGuid():N}";
        var activeFile   = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            // Publish then Mark Expired (same flow as SharedDocumentExpireTests) — Status is what
            // drives the Expired bucket, independent of Next Review Date.
            await UploadDocumentWithReviewDateAsync(
                expiredTitle, expiredFile, DateOnly.FromDateTime(DateTime.Today.AddDays(10)));
            var expiredDocumentId = await GetUploadedDocumentIdAsync(expiredTitle);
            await detail.GoToAsync(AcmeId, expiredDocumentId);
            await detail.PublishAsync();
            await detail.ExpireAsync();
            Assert.Equal("Expired", await detail.GetStatusAsync());

            // Still Published with a Next Review Date set — control that must NOT show up under
            // "Expired".
            await UploadDocumentWithReviewDateAsync(
                activeTitle, activeFile, DateOnly.FromDateTime(DateTime.Today.AddDays(10)));
            var activeDocumentId = await GetUploadedDocumentIdAsync(activeTitle);
            await detail.GoToAsync(AcmeId, activeDocumentId);
            await detail.PublishAsync();

            await GoToListPageAsync();
            await SelectReviewStatusAsync("Expired");

            Assert.True(await IsTitleVisibleInGridAsync(expiredTitle),
                "Expected the expired document to appear under the Expired filter");
            Assert.False(await IsTitleVisibleInGridAsync(activeTitle),
                "Expected the still-published document to be excluded from the Expired filter");
        }
        finally
        {
            if (File.Exists(expiredFile)) File.Delete(expiredFile);
            if (File.Exists(activeFile)) File.Delete(activeFile);
        }
    }

    [Fact]
    public async Task ReviewStatusFilter_SwitchingBetweenBuckets_ChangesGridResults()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var dueSoonTitle = $"Test Policy {Guid.NewGuid():N}";
        var dueSoonFile  = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        var noDateTitle  = $"Test Policy {Guid.NewGuid():N}";
        var noDateFile   = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentWithReviewDateAsync(
                dueSoonTitle, dueSoonFile, DateOnly.FromDateTime(DateTime.Today.AddDays(3)));
            await UploadDocumentWithReviewDateAsync(noDateTitle, noDateFile, reviewDate: null);

            await GoToListPageAsync();

            await SelectReviewStatusAsync("Due Soon");
            Assert.True(await IsTitleVisibleInGridAsync(dueSoonTitle));
            Assert.False(await IsTitleVisibleInGridAsync(noDateTitle));

            await SelectReviewStatusAsync("No Review");
            Assert.False(await IsTitleVisibleInGridAsync(dueSoonTitle),
                "Expected switching the Review Status filter to No Review to remove the Due Soon document from the grid");
            Assert.True(await IsTitleVisibleInGridAsync(noDateTitle),
                "Expected switching the Review Status filter to No Review to bring the no-review-date document back into the grid");
        }
        finally
        {
            if (File.Exists(dueSoonFile)) File.Delete(dueSoonFile);
            if (File.Exists(noDateFile)) File.Delete(noDateFile);
        }
    }

    private ILocator ReviewStatusCombobox =>
        _page.Locator(".col-md-2").Filter(new() { HasText = "Review Status" }).Locator("span[role='combobox']").First;

    private async Task GoToListPageAsync()
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });
        await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 15_000 });
    }

    // Selects the given option in the "Review Status" SfDropDownList and waits for the grid to
    // settle after the resulting reload, mirroring the Category/Review Frequency combobox
    // interaction pattern used throughout this test suite (click combobox, wait for popup, click
    // item).
    private async Task SelectReviewStatusAsync(string optionLabel)
    {
        await ReviewStatusCombobox.ClickAsync();
        await _page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await _page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = optionLabel })
            .First
            .ClickAsync();
        await _page.WaitForSelectorAsync(".e-popup.e-ddl:visible",
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 15_000 });
    }

    private async Task<bool> IsTitleVisibleInGridAsync(string title)
    {
        return await _page.Locator(".e-row").Filter(new() { HasText = title }).CountAsync() > 0;
    }

    // Reads the document id straight from the list row's link href, avoiding a separate
    // click+navigate+wait round trip (same pattern as SharedDocumentExpireTests /
    // SharedDocumentPublishTests.GetUploadedDocumentIdAsync).
    private async Task<Guid> GetUploadedDocumentIdAsync(string title)
    {
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = title }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        return Guid.Parse(href.Split('/').Last());
    }

    // Uploads a shared document from the Shared Documents list page with an optional Next Review
    // Date (left unset when reviewDate is null), leaving Review Frequency at its "None" default —
    // same upload-dialog interaction pattern as HrDashboardTests.UploadDocumentWithReviewDateAsync
    // / SharedDocumentListReviewColumnsTests.UploadDocumentAsync.
    private async Task UploadDocumentWithReviewDateAsync(string title, string filePath, DateOnly? reviewDate)
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("Document title").FillAsync(title);

        // Type into the filter input before clicking (same pattern as SelectFilterAsync/
        // SelectReviewStatusAsync elsewhere in this suite) rather than clicking the option
        // immediately after opening the popup. This dropdown has AllowFiltering="true" and
        // clicking an option right after open — before typing settles the list — can catch
        // Syncfusion mid-render, detaching the target <li> out from under the click ("element was
        // detached from the DOM, retrying").
        var categoryGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await categoryGroup.Locator("span[role='combobox']").First.ClickAsync();
        await _page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        var categoryFilterInput = _page.Locator(".e-popup.e-ddl:visible input.e-input").First;
        await categoryFilterInput.FillAsync("Policy");
        await _page.WaitForSelectorAsync(".e-popup.e-ddl .e-list-item:not(.e-hide)", new() { Timeout = 15_000 });
        await _page.Locator(".e-popup.e-ddl .e-list-item:not(.e-hide)")
            .Filter(new() { HasText = "Policy" })
            .First
            .ClickAsync();

        if (reviewDate is not null)
        {
            var reviewDateInput = dialog.Locator(".col-md-6")
                .Filter(new() { HasText = "Next Review Date" })
                .Locator(".e-date-wrapper input.e-input");
            await reviewDateInput.ClickAsync();
            await reviewDateInput.FillAsync(reviewDate.Value.ToString("dd/MM/yyyy"));
            await _page.Keyboard.PressAsync("Tab");
        }

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
