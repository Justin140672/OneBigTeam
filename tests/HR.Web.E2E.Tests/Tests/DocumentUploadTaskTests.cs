using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class DocumentUploadTaskTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId   = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId    = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid CarlosId = Guid.Parse("30000000-0000-0000-0000-000000000010");

    private static readonly Guid CarlosUploadTaskId = Guid.Parse("a0000000-0000-0000-0000-000000000011");

    private const string LauraEmail  = "laura.bennett@acme.example";
    private const string TomEmail    = "tom.williams@acme.example";
    private const string CarlosEmail = "carlos.rivera@acme.example";

    [Fact]
    public async Task DocumentUploadTask_ShowsUploadPanel_WhenTaskIsOpen()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CarlosEmail);

        await taskView.GoToAsync(AcmeId, CarlosId, CarlosUploadTaskId);

        Assert.True(await taskView.HasDocumentUploadPanelAsync(),
            "Expected the document upload panel to be shown for an Upload/Document task");

        // Leave and probation panels must NOT appear on a document upload task.
        Assert.False(await taskView.HasLeaveReviewPanelAsync(),
            "Leave review panel should not appear on a document upload task");
        Assert.False(await taskView.HasProbationReviewPanelAsync(),
            "Probation review panel should not appear on a document upload task");
    }

    [Fact]
    public async Task AssignedUploadTask_MarksTaskAsCompleted()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);
        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenTasksTabAsync();
        var alreadyRequested = (await profile.GetTaskTitlesAsync())
            .Any(t => t.Contains("Upload Certificate", StringComparison.Ordinal));

        if (!alreadyRequested)
        {
            await login.SwitchAccountAsync(LauraEmail);
            await empAdmin.GoToAsync(AcmeId, TomId);
            await empAdmin.OpenDocumentsTabAsync();
            await empAdmin.RequestDocumentAsync("Certificate");

            await login.LoginAsync(TomEmail);
            await profile.GoToAsync(AcmeId, TomId);
            await profile.OpenTasksTabAsync();
        }

        await profile.ClickTaskAsync("Upload Certificate");
        await taskView.WaitForLoadedAsync();

        if (await taskView.GetStatusAsync() == "Completed")
        {
            return;
        }

        Assert.True(await taskView.HasDocumentUploadPanelAsync(),
            "Expected the document upload panel to be visible");

        var tempFile = Path.Combine(Path.GetTempPath(), $"passport-{Guid.NewGuid():N}.pdf");
        try
        {
            var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
            var pdf   = new byte[magic.Length + 1020];
            magic.CopyTo(pdf, 0);
            await File.WriteAllBytesAsync(tempFile, pdf);

            await taskView.AttachUploadFileAsync(tempFile);
            await taskView.SubmitDocumentUploadAsync();

            Assert.Equal("Completed", await taskView.GetStatusAsync());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
