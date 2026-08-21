using System.Net.Http.Json;
using System.Text.RegularExpressions;
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
///
/// AssetAcknowledgementTask_AcknowledgeReceipt_CompletesTask below is the one mutating test in
/// this class — completing an acknowledgement task is irreversible (no "un-acknowledge" action),
/// so it no longer touches Tom's shared seeded task (that used to permanently flip Tom's task to
/// Completed for whichever test happened to run second under parallel execution, and every other
/// test in this class expects it to still be Not Started). It creates and logs in as its own
/// fresh employee via EnsureEmployeeLoginAsync instead — see that helper's remarks for how a
/// freshly-created employee gets a real, working login.
/// </summary>
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
    // Seeded asset category for Acme (see AssetsModule.cs seed data / AssetEditCloseBehaviorTests).
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

    /// <summary>
    /// Creates a brand-new employee via the standard New Employee form and returns their id and
    /// work email, captured from the URL after navigating back into their profile from the
    /// employee list. Caller must already be logged in as an HR administrator.
    /// </summary>
    private async Task<(Guid EmployeeId, string Email, string LastName)> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, string suffix)
    {
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"AssetAck{suffix}{unique}";
        var workEmail = $"e2e.assetack.{suffix.ToLowerInvariant()}{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");

        await empEdit.SaveNewEmployeeAsync();
        await empList.ClickEmployeeAsync(lastName);

        var match = Regex.Match(_page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return (Guid.Parse(match.Groups[1].Value), workEmail, lastName);
    }

    /// <summary>
    /// Gives a freshly-created employee a real, working Supabase login via the dev-only
    /// POST /api/dev/ensure-employee-login endpoint (HR.Modules.Identity's DevEnsureEmployeeLogin
    /// feature), which idempotently calls the same IdentityModule.EnsureDevSupabaseUserAsync
    /// building block used to seed the four canonical dev personas (Laura Bennett, James Okafor,
    /// Marcus Diallo, Tom Williams) — see DevPersonaStore.Personas and
    /// IdentityModule.SeedDevSupabaseUsersAsync. A brand-new Employee row has no linked UserProfile
    /// by construction (employee creation alone never provisions one; only a completed invite
    /// does), so without this call there would be no way to log in AS this employee at all short of
    /// driving the full invite-accept-password-setup UI flow, which doesn't exist in this suite.
    /// 404s outside Development, matching every other /api/dev/* endpoint.
    /// </summary>
    private async Task EnsureEmployeeLoginAsync(Guid employeeId, string email, string lastName)
    {
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

        // This endpoint's EnsureDevSupabaseUserAsync makes a real, network-dependent call to
        // Supabase's Admin API (unlike most other E2E auth paths, which are faked under
        // E2E_TESTING=true — see FakeSupabaseAuthGateway's own remarks) — a genuine transient
        // failure/rate-limit response under this suite's concurrency can surface as a 500 here.
        // Retry a couple of times before failing outright, and capture the response body on
        // failure so a real, non-transient error is immediately diagnosable.
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
        var empList   = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit   = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var empAdmin  = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);
        var assetEdit = new AssetEditPage(_page, _fixture.WebBaseUrl);
        var taskView  = new TaskViewPage(_page, _fixture.WebBaseUrl);

        // Arrange (as HR admin): a fresh employee, a fresh available asset (avoids contending with
        // the single shared seeded ASSET-0003 that other tests permanently consume), and assigning
        // it to the employee — which auto-creates the "Acknowledge receipt of asset" task.
        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var (employeeId, email, lastName) = await CreateEmployeeAsync(empList, empEdit, "Ack");

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

        // Act: log in AS the fresh employee and acknowledge the asset.
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
