using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class SharedDocumentListReviewColumnsTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";
    private const string MarcusDiallo = "Marcus Diallo";

    private const int ReviewFrequencyColumnIndex = 6;
    private const int ReviewOwnerColumnIndex = 7;

    [Fact]
    public async Task ListGrid_WithReviewFrequencyAndReviewOwner_ShowsBothInTheirColumns()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(
                title, tempFile,
                reviewFrequencyLabel: "Quarterly",
                reviewOwnerNameFragment: MarcusDiallo);

            await GoToListPageAsync();

            Assert.Equal("Quarterly", await GetListRowCellAsync(title, ReviewFrequencyColumnIndex));
            Assert.Equal(MarcusDiallo, await GetListRowCellAsync(title, ReviewOwnerColumnIndex));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ListGrid_WithNoReviewFrequencyOrReviewOwner_ShowsBlankCells()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile);

            await GoToListPageAsync();

            Assert.Equal(string.Empty, await GetListRowCellAsync(title, ReviewFrequencyColumnIndex));
            Assert.Equal(string.Empty, await GetListRowCellAsync(title, ReviewOwnerColumnIndex));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ListGrid_WithSixMonthlyReviewFrequency_DisplaysFriendlyLabel()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title    = $"Test Policy {Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await UploadDocumentAsync(title, tempFile, reviewFrequencyLabel: "Six Monthly");

            await GoToListPageAsync();

            Assert.Equal("Six Monthly", await GetListRowCellAsync(title, ReviewFrequencyColumnIndex));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private async Task GoToListPageAsync()
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });
        await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 15_000 });
    }

    private async Task<string> GetListRowCellAsync(string title, int columnIndex)
    {
        var row = _page.Locator(".e-row").Filter(new() { HasText = title }).First;
        return (await row.Locator(".e-rowcell").Nth(columnIndex).InnerTextAsync()).Trim();
    }

    private async Task UploadDocumentAsync(
        string title, string filePath,
        string? reviewFrequencyLabel = null,
        string? reviewOwnerNameFragment = null)
    {
        await _page.GotoAsync(_fixture.WebBaseUrl + $"/companies/{AcmeId}/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.GetByPlaceholder("Document title").FillAsync(title);

        var categoryGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await DropDownSelector.SelectAsync(_page, categoryGroup, "Policy");

        if (reviewFrequencyLabel is not null)
        {
            var reviewFrequencyGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Review Frequency" });
            await DropDownSelector.SelectAsync(_page, reviewFrequencyGroup, reviewFrequencyLabel);

            var reviewDateInput = dialog.Locator(".col-md-6")
                .Filter(new() { HasText = "Next Review Date" })
                .Locator(".e-date-wrapper input.e-input");
            await reviewDateInput.ClickAsync();
            await reviewDateInput.FillAsync(DateOnly.FromDateTime(DateTime.Today.AddYears(1)).ToString("dd/MM/yyyy"));
            await _page.Keyboard.PressAsync("Tab");
        }

        if (reviewOwnerNameFragment is not null)
        {
            var reviewOwnerGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Review Owner" });
            await DropDownSelector.SelectAsync(_page, reviewOwnerGroup, reviewOwnerNameFragment);
        }

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
