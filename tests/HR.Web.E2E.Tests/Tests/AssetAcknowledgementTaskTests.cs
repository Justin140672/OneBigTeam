using System.Net.Http.Json;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class AssetAcknowledgementTaskTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId              = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId               = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid CarlosId            = Guid.Parse("30000000-0000-0000-0000-000000000010");
    private static readonly Guid TomAssetTaskId      = Guid.Parse("a0000000-0000-0000-0000-000000000020");
    private static readonly Guid CarlosUploadTaskId  = Guid.Parse("a0000000-0000-0000-0000-000000000011");

    private const string TomEmail   = "tom.williams@acme.example";
    private const string CarlosEmail = "carlos.rivera@acme.example";
    private const string HrAdminEmail = "laura.bennett@acme.example";
    private const string SeededCategory = "IT Equipment";

    [Fact]
    public async Task AssetAcknowledgementTask_ShowsAcknowledgementPanel()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await taskView.GoToAsync(AcmeId, TomId, TomAssetTaskId);

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

        await taskView.GoToAsync(AcmeId, TomId, TomAssetTaskId);

        Assert.True(await taskView.HasAssetAcknowledgementPanelAsync(),
            "Expected the acknowledgement panel to be visible");

        var assetNumber = await taskView.GetAcknowledgementAssetNumberAsync();
        Assert.False(string.IsNullOrWhiteSpace(assetNumber),
            "Expected the asset number to be displayed in the acknowledgement panel");
        Assert.Contains("ASSET-0001", assetNumber, StringComparison.OrdinalIgnoreCase);
    }

    private static (Guid EmployeeId, string Email, string LastName) CreateEmployeeAsync()
    {
        var seeded = SeededE2eEmployees.AssetAcknowledgement;
        return (seeded.EmployeeId, seeded.Email, seeded.LastName);
    }

    private async Task EnsureEmployeeLoginAsync(Guid employeeId, string email, string lastName)
    {
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

        HttpResponseMessage? response = null;
        string? body = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            response = await http.PostAsJsonAsync("/api/dev/ensure-employee-login", new
            {
                EmployeeId = employeeId,
                CompanyId  = AcmeId,
                Email      = email,
                FirstName  = "E2E",
                LastName   = lastName,
            });

            if (response.IsSuccessStatusCode) return;

            body = await response.Content.ReadAsStringAsync();
            if (attempt < 3) await Task.Delay(1000 * attempt);
        }

        Assert.True(response!.IsSuccessStatusCode,
            $"Expected /api/dev/ensure-employee-login to succeed, got {response.StatusCode}. Response body: {body}");
    }

    [Fact]
    public async Task AssetAcknowledgementTask_AcknowledgeReceipt_CompletesTask()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin  = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);
        var assetEdit = new AssetEditPage(_page, _fixture.WebBaseUrl);
        var taskView  = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var (employeeId, email, lastName) = CreateEmployeeAsync();

        var assetNumber = $"E2E-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var assetName   = $"E2E Acknowledge Asset {Guid.NewGuid().ToString("N")[..8]}";
        await assetEdit.GoToNewAsync(AcmeId);
        await assetEdit.FillAssetNumberAsync(assetNumber);
        await assetEdit.FillNameAsync(assetName);
        await assetEdit.SelectCategoryAsync(SeededCategory);
        await assetEdit.SaveAsync();

        await empAdmin.GoToAsync(AcmeId, employeeId);
        await empAdmin.OpenAssetsTabAsync();
        await empAdmin.OpenAssignAssetDialogAsync();
        await empAdmin.SelectAssetAndConfirmAsync(assetNumber);

        await EnsureEmployeeLoginAsync(employeeId, email, lastName);

        await login.LoginAsync(email);
        await taskView.GoToByTitleAsync(AcmeId, employeeId, "Acknowledge receipt of asset");

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

        await taskView.GoToAsync(AcmeId, CarlosId, CarlosUploadTaskId);

        Assert.False(await taskView.HasAssetAcknowledgementPanelAsync(),
            "Asset acknowledgement panel must not appear on a document upload task");

        Assert.True(await taskView.HasDocumentUploadPanelAsync(),
            "Expected the document upload panel on an Upload/Document task");
    }
}
