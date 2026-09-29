using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class SharedDocumentUploadTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    private const string HrEmail      = "laura.bennett@acme.example";
    private const string ManagerEmail = "james.okafor@acme.example";

    [Fact]
    public async Task HrAdministrator_CanUploadSharedDocument_AndSeeItInList()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        await _page.GotoAsync(_fixture.WebBaseUrl + "/companies/00000000-0000-0000-0000-000000000001/shared-documents");
        await _page.WaitForSelectorAsync("h1:has-text('Shared Documents')", new() { Timeout = 15_000 });

        await _page.GetByRole(AriaRole.Button, new() { Name = "Upload Document" }).ClickAsync();

        var dialog = _page.GetByRole(AriaRole.Dialog, new() { Name = "Upload Document" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var title = $"Test Policy {Guid.NewGuid():N}";
        await dialog.GetByPlaceholder("Document title").FillAsync(title);

        var categoryGroup = dialog.Locator(".col-md-6").Filter(new() { HasText = "Category" });
        await DropDownSelector.SelectAsync(_page, categoryGroup, "Policy");

        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(tempFile, BuildTestPdf());
            await dialog.Locator("input[type='file']").SetInputFilesAsync(tempFile);

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
            await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 30_000 });

            await _page.WaitForSelectorAsync($"text={title}", new() { Timeout = 15_000 });
            var row = _page.Locator(".e-row").Filter(new() { HasText = title });
            await Microsoft.Playwright.Assertions.Expect(row).ToContainTextAsync("Draft");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Manager_CannotReach_SharedDocumentsPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(ManagerEmail);

        var target = _fixture.WebBaseUrl + "/companies/00000000-0000-0000-0000-000000000001/shared-documents";
        await _page.GotoAsync(target);

        try
        {
            await _page.WaitForURLAsync(u => !u.Contains("/shared-documents"), new() { Timeout = 25_000 });
        }
        catch (TimeoutException) { /* fall through to assert on the resulting URL */ }

        // IsHrAdministrator-only page guard redirects away — James (Employee+Manager) must not
        // land on or see the Shared Documents page.
        Assert.DoesNotContain("/shared-documents", _page.Url);
    }

    private static byte[] BuildTestPdf()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }
}
