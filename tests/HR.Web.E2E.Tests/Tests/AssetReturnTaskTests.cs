using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the asset return task panel on the Task View page.
///
/// Uses seeded data:
///   - Sarah Chen (30000000-0000-0000-0000-000000000001) has a seeded asset
///     return task (a0000000-0000-0000-0000-000000000022) linked to
///     AssetAssignment (c0000000-0000-0000-0000-000000000005) for a Dell UltraSharp 27".
/// Tom's acknowledgement task is used for the "wrong panel" assertion (read-only).
/// </summary>
[Collection("E2E")]
public sealed class AssetReturnTaskTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId             = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahReturnTaskId  = Guid.Parse("a0000000-0000-0000-0000-000000000022");
    private static readonly Guid TomAcknowledgeTaskId = Guid.Parse("a0000000-0000-0000-0000-000000000020");

    private const string SarahEmail = "sarah.chen@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";

    [Fact]
    public async Task AssetReturnTask_ShowsReturnPanel()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await taskView.GoToAsync(AcmeId, SarahReturnTaskId);

        Assert.True(await taskView.HasAssetReturnPanelAsync(),
            "Expected the asset return panel for a Return/Asset task");

        Assert.False(await taskView.HasAssetAcknowledgementPanelAsync(),
            "Acknowledgement panel must not appear on a return task");

        Assert.False(await taskView.HasLeaveReviewPanelAsync(),
            "Leave review panel must not appear on a return task");

        Assert.False(await taskView.HasDocumentUploadPanelAsync(),
            "Document upload panel must not appear on a return task");
    }

    [Fact]
    public async Task AssetReturnTask_ShowsAssetDetails()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await taskView.GoToAsync(AcmeId, SarahReturnTaskId);

        Assert.True(await taskView.HasAssetReturnPanelAsync(),
            "Expected the return panel to be visible");

        var assetNumber = await taskView.GetReturnAssetNumberAsync();
        Assert.False(string.IsNullOrWhiteSpace(assetNumber),
            "Expected the asset number to be displayed in the return panel");
        Assert.Contains("ASSET-0002", assetNumber, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AssetReturnTask_ConfirmReturn_CompletesTask()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await taskView.GoToAsync(AcmeId, SarahReturnTaskId);

        Assert.True(await taskView.HasAssetReturnPanelAsync(),
            "Expected the return panel before confirming return");

        var statusBefore = await taskView.GetStatusAsync();
        Assert.NotEqual("Completed", statusBefore);

        await taskView.ConfirmReturnAsync();

        Assert.Equal("Completed", await taskView.GetStatusAsync());
    }

    [Fact]
    public async Task AcknowledgementTask_DoesNotShowReturnPanel()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await taskView.GoToAsync(AcmeId, TomAcknowledgeTaskId);

        Assert.False(await taskView.HasAssetReturnPanelAsync(),
            "Asset return panel must not appear on an acknowledgement task");

        Assert.True(await taskView.HasAssetAcknowledgementPanelAsync(),
            "Expected the acknowledgement panel on an Acknowledge/Asset task");
    }
}
