using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the asset acknowledgement task panel on the Task View page.
///
/// Uses seeded data:
///   - Tom Williams (30000000-0000-0000-0000-000000000004) has a seeded asset
///     acknowledgement task (a0000000-0000-0000-0000-000000000020) linked to
///     AssetAssignment (c0000000-0000-0000-0000-000000000003) for a MacBook Pro.
/// Carlos Rivera's upload task is used for the "wrong panel" assertion (read-only).
/// </summary>
[Collection("E2E")]
public sealed class AssetAcknowledgementTaskTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId              = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomAssetTaskId      = Guid.Parse("a0000000-0000-0000-0000-000000000020");
    private static readonly Guid CarlosUploadTaskId  = Guid.Parse("a0000000-0000-0000-0000-000000000011");

    private const string TomEmail   = "tom.williams@acme.example";
    private const string CarlosEmail = "carlos.rivera@acme.example";

    [Fact]
    public async Task AssetAcknowledgementTask_ShowsAcknowledgementPanel()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await taskView.GoToAsync(AcmeId, TomAssetTaskId);

        Assert.True(await taskView.HasAssetAcknowledgementPanelAsync(),
            "Expected the asset acknowledgement panel for an Acknowledge/Asset task");

        Assert.False(await taskView.HasLeaveReviewPanelAsync(),
            "Leave review panel must not appear on an asset acknowledgement task");

        Assert.False(await taskView.HasDocumentUploadPanelAsync(),
            "Document upload panel must not appear on an asset acknowledgement task");
    }

    [Fact]
    public async Task AssetAcknowledgementTask_ShowsAssetDetails()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await taskView.GoToAsync(AcmeId, TomAssetTaskId);

        Assert.True(await taskView.HasAssetAcknowledgementPanelAsync(),
            "Expected the acknowledgement panel to be visible");

        var assetNumber = await taskView.GetAcknowledgementAssetNumberAsync();
        Assert.False(string.IsNullOrWhiteSpace(assetNumber),
            "Expected the asset number to be displayed in the acknowledgement panel");
        Assert.Contains("ASSET-0001", assetNumber, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AssetAcknowledgementTask_AcknowledgeReceipt_CompletesTask()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await taskView.GoToAsync(AcmeId, TomAssetTaskId);

        Assert.True(await taskView.HasAssetAcknowledgementPanelAsync(),
            "Expected the acknowledgement panel before acknowledging");

        var statusBefore = await taskView.GetStatusAsync();
        Assert.NotEqual("Completed", statusBefore);

        await taskView.AcknowledgeAssetAsync();

        Assert.Equal("Completed", await taskView.GetStatusAsync());
    }

    [Fact]
    public async Task DocumentUploadTask_DoesNotShowAcknowledgementPanel()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CarlosEmail);

        await taskView.GoToAsync(AcmeId, CarlosUploadTaskId);

        Assert.False(await taskView.HasAssetAcknowledgementPanelAsync(),
            "Asset acknowledgement panel must not appear on a document upload task");

        Assert.True(await taskView.HasDocumentUploadPanelAsync(),
            "Expected the document upload panel on an Upload/Document task");
    }
}
