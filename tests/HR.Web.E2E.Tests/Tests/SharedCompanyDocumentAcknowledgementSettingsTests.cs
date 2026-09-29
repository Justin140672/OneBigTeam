using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class SharedCompanyDocumentAcknowledgementSettingsTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task CompanySettings_UpdateDefaultAcknowledgementStatement_PersistsAfterReload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        await HrSettingsSerialTestBase.GateInstance.WaitAsync();
        try
        {
            await hrSettings.GoToAsync(AcmeId);

            var statement = $"Default acknowledgement statement {Guid.NewGuid():N}";
            await hrSettings.SetDefaultAcknowledgementStatementAsync(statement);

            await hrSettings.SaveAsync();
            Assert.False(await hrSettings.HasErrorAsync(),
                "Expected no error after saving the default acknowledgement statement");

            await hrSettings.GoToAsync(AcmeId);

            Assert.Equal(statement, await hrSettings.GetDefaultAcknowledgementStatementAsync());
        }
        finally
        {
            HrSettingsSerialTestBase.GateInstance.Release();
        }
    }

    [Fact]
    public async Task UploadDialog_RequiresAcknowledgement_AutoPopulatesFromCompanyDefault_AndShowsLivePreview()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);
        var upload = new UploadSharedCompanyDocumentDialogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        await HrSettingsSerialTestBase.GateInstance.WaitAsync();
        try
        {
            var defaultStatement = $"Default statement {Guid.NewGuid():N}";
            await hrSettings.GoToAsync(AcmeId);
            await hrSettings.SetDefaultAcknowledgementStatementAsync(defaultStatement);
            await hrSettings.SaveAsync();
            Assert.False(await hrSettings.HasErrorAsync());

            await upload.GoToListAsync(AcmeId);
            await upload.OpenAsync();

            await upload.FillTitleAsync($"Test Policy {Guid.NewGuid():N}");
            await upload.SelectCategoryAsync("Policy");

            await upload.CheckRequiresAcknowledgementAsync();

            Assert.Equal(defaultStatement, await upload.GetAcknowledgementStatementValueAsync());
            Assert.Equal(defaultStatement, await upload.GetAcknowledgementPreviewTextAsync());
        }
        finally
        {
            HrSettingsSerialTestBase.GateInstance.Release();
        }
    }

    [Fact]
    public async Task UploadDialog_BlocksSave_WhenAcknowledgementStatementClearedWhileRequired()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var upload = new UploadSharedCompanyDocumentDialogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        await upload.GoToListAsync(AcmeId);
        await upload.OpenAsync();

        await upload.FillTitleAsync($"Test Policy {Guid.NewGuid():N}");
        await upload.SelectCategoryAsync("Policy");

        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(tempFile, BuildTestPdf());
            await upload.SetFileAsync(tempFile);

            await upload.CheckRequiresAcknowledgementAsync();
            await upload.FillAcknowledgementStatementAsync("");

            await upload.ClickUploadAsync();

            Assert.True(await upload.IsOpenAsync(),
                "Expected the dialog to remain open when the acknowledgement statement is blank while required");

            var error = await upload.GetGlobalErrorAsync();
            Assert.NotNull(error);
            Assert.Contains("acknowledgement statement is required", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Draft_HR_CanFreelyEditAcknowledgementStatement()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var documentId = await UploadDraftDocumentAsync();

        await detail.GoToAsync(AcmeId, documentId);
        Assert.Equal("Draft", await detail.GetStatusAsync());

        await detail.RequireAcknowledgementAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));

        await detail.OpenEditAcknowledgementDialogAsync();
        Assert.False(await detail.IsAcknowledgementStatementReadOnlyAsync(),
            "Expected the statement field to be editable while the document is still Draft");

        var newStatement = $"Updated statement {Guid.NewGuid():N}";
        await detail.FillAcknowledgementStatementAsync(newStatement);
        await detail.SaveEditAcknowledgementDialogAsync();

        await detail.OpenEditAcknowledgementDialogAsync();
        Assert.Equal(newStatement, await detail.GetAcknowledgementStatementValueAsync());
    }

    [Fact]
    public async Task Published_AcknowledgementStatementField_IsLockedWithExplanatoryNote()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var documentId = await UploadDraftDocumentAsync();

        await detail.GoToAsync(AcmeId, documentId);
        await detail.RequireAcknowledgementAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));
        await detail.PublishAsync();
        Assert.Equal("Published", await detail.GetStatusAsync());

        await detail.OpenEditAcknowledgementDialogAsync();

        Assert.True(await detail.IsAcknowledgementStatementReadOnlyAsync(),
            "Expected the statement field to be read-only once the document is Published");
        Assert.True(await detail.IsAcknowledgementLockedNoteVisibleAsync(),
            "Expected the 'locked after publishing' explanatory note to be visible");
        Assert.True(await detail.IsResetAcknowledgementStatementButtonDisabledAsync(),
            "Expected the 'Reset to Default' button to be disabled once the field is locked");
    }

    [Fact]
    public async Task EditDialog_ResetToDefault_RestoresCompanyDefaultStatement()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        await HrSettingsSerialTestBase.GateInstance.WaitAsync();
        try
        {
            var defaultStatement = $"Default statement {Guid.NewGuid():N}";
            await hrSettings.GoToAsync(AcmeId);
            await hrSettings.SetDefaultAcknowledgementStatementAsync(defaultStatement);
            await hrSettings.SaveAsync();
            Assert.False(await hrSettings.HasErrorAsync());

            var documentId = await UploadDraftDocumentAsync();

            await detail.GoToAsync(AcmeId, documentId);
            await detail.RequireAcknowledgementAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));

            await detail.OpenEditAcknowledgementDialogAsync();
            await detail.FillAcknowledgementStatementAsync($"Custom statement {Guid.NewGuid():N}");
            await detail.SaveEditAcknowledgementDialogAsync();

            await detail.OpenEditAcknowledgementDialogAsync();
            await detail.ClickResetAcknowledgementStatementToDefaultAsync();

            Assert.Equal(defaultStatement, await detail.GetAcknowledgementStatementValueAsync());
        }
        finally
        {
            HrSettingsSerialTestBase.GateInstance.Release();
        }
    }

    [Fact]
    public async Task AuditHistoryDialog_OpensFromDetailPage_AndShowsEntry_AfterPublish()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new SharedDocumentDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrEmail);

        var title = $"Test Policy {Guid.NewGuid():N}";
        var documentId = await UploadDraftDocumentAsync(title);

        await detail.GoToAsync(AcmeId, documentId);
        await detail.PublishAsync();

        await detail.OpenAuditHistoryDialogAsync();
        Assert.True(await detail.IsAuditHistoryDialogOpenAsync());

        var rowCount = await detail.GetAuditHistoryRowCountAsync();
        Assert.True(rowCount >= 1,
            $"Expected at least one audit history entry after uploading and publishing, got {rowCount}");

        await detail.ClickViewAuditHistoryRowAsync(title);
        Assert.True(await detail.IsAuditDetailDialogOpenAsync());

        var dialogText = await detail.GetAuditDetailDialogTextAsync();
        Assert.Contains(title, dialogText);

        await detail.CloseAuditDetailDialogAsync();
        Assert.False(await detail.IsAuditDetailDialogOpenAsync());

        await detail.CloseAuditHistoryDialogAsync();
        Assert.False(await detail.IsAuditHistoryDialogOpenAsync());
    }

    private async Task<Guid> UploadDraftDocumentAsync(string? title = null)
    {
        title ??= $"Test Policy {Guid.NewGuid():N}";
        var upload = new UploadSharedCompanyDocumentDialogPage(_page, _fixture.WebBaseUrl);
        var tempFile = Path.Combine(Path.GetTempPath(), $"shared-doc-{Guid.NewGuid():N}.pdf");
        try
        {
            await upload.GoToListAsync(AcmeId);
            await upload.OpenAsync();

            await upload.FillTitleAsync(title);
            await upload.SelectCategoryAsync("Policy");

            await File.WriteAllBytesAsync(tempFile, BuildTestPdf());
            await upload.SetFileAsync(tempFile);

            await upload.UploadAndWaitForCloseAsync();

            return await upload.GetUploadedDocumentIdAsync(title);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static byte[] BuildTestPdf()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }
}
