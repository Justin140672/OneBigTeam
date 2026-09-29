using System.Net.Http.Json;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class AssetReturnTaskTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId             = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahId            = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId              = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid SarahReturnTaskId  = Guid.Parse("a0000000-0000-0000-0000-000000000022");
    private static readonly Guid TomAcknowledgeTaskId = Guid.Parse("a0000000-0000-0000-0000-000000000020");

    private const string SarahEmail = "sarah.chen@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";
    private const string HrAdminEmail = "laura.bennett@acme.example";
    private const string SeededCategory = "IT Equipment";

    [Fact]
    public async Task AssetReturnTask_ShowsReturnPanel()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await taskView.GoToAsync(AcmeId, SarahId, SarahReturnTaskId);

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

        await taskView.GoToAsync(AcmeId, SarahId, SarahReturnTaskId);

        Assert.True(await taskView.HasAssetReturnPanelAsync(),
            "Expected the return panel to be visible");

        var assetNumber = await taskView.GetReturnAssetNumberAsync();
        Assert.False(string.IsNullOrWhiteSpace(assetNumber),
            "Expected the asset number to be displayed in the return panel");
        Assert.Contains("ASSET-0002", assetNumber, StringComparison.OrdinalIgnoreCase);
    }

    private static (Guid EmployeeId, string Email, string LastName) CreateEmployeeAsync()
    {
        var seeded = SeededE2eEmployees.AssetReturn;
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
    public async Task AssetReturnTask_ConfirmReturn_CompletesTask()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin  = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);
        var assetEdit = new AssetEditPage(_page, _fixture.WebBaseUrl);
        var taskView  = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var (employeeId, email, lastName) = CreateEmployeeAsync();

        var assetNumber = $"E2E-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var assetName   = $"E2E Return Asset {Guid.NewGuid().ToString("N")[..8]}";
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
        await taskView.AcknowledgeAssetAsync();
        Assert.Equal("Completed", await taskView.GetStatusAsync());
        await taskView.CloseAsync();

        await login.LoginAsync(HrAdminEmail);
        await empAdmin.GoToAsync(AcmeId, employeeId);
        await empAdmin.OpenAssetsTabAsync();
        await empAdmin.OpenReturnAssetDialogAsync();
        await empAdmin.SelectAssetAndConfirmReturnAsync(assetNumber);

        await login.LoginAsync(email);
        await taskView.GoToByTitleAsync(AcmeId, employeeId, "Return asset");

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

        await taskView.GoToAsync(AcmeId, TomId, TomAcknowledgeTaskId);

        Assert.False(await taskView.HasAssetReturnPanelAsync(),
            "Asset return panel must not appear on an acknowledgement task");

        Assert.True(await taskView.HasAssetAcknowledgementPanelAsync(),
            "Expected the acknowledgement panel on an Acknowledge/Asset task");
    }
}
