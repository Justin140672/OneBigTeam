using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CompleteProfilePhotoReviewTaskEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ManagerUser = Guid.Parse("bb100002-0000-0000-0000-000000000001");

    public CompleteProfilePhotoReviewTaskEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Completing_Review_Task_Via_Generic_Endpoint_With_Approve_Promotes_Pending_Photo_To_Live()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await SeedManagerAsync(companyId);

        using (var selfClient = await SelfClient(companyId, employeeId))
        {
            var upload = await selfClient.PostAsync(
                $"/api/companies/{companyId}/employees/me/profile-photo",
                BuildPngUpload("submitted.png"));
            Assert.Equal(System.Net.HttpStatusCode.OK, upload.StatusCode);
        }

        using var managerClient = await ManagerClient(companyId);
        var task = await FindProfilePhotoReviewTaskAsync(managerClient, companyId);

        var completeResp = await managerClient.PostAsync(
            $"/api/companies/{companyId}/tasks/{task.Id}/complete",
            Json(new { outcomeDecision = "Approve" }));
        Assert.True(completeResp.IsSuccessStatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        var liveRows = await db.EmployeeProfilePhotos.Where(p => p.EmployeeId == employeeId).ToListAsync();
        Assert.Single(liveRows);
        Assert.Equal("submitted.png", liveRows[0].FileName);

        var pendingRows = await db.PendingProfilePhotos.Where(p => p.EmployeeId == employeeId).ToListAsync();
        Assert.Empty(pendingRows);
    }

    [Fact]
    public async Task Completing_Review_Task_Via_Generic_Endpoint_Without_OutcomeDecision_Fails_And_Leaves_Pending_Untouched()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await SeedManagerAsync(companyId);

        using (var selfClient = await SelfClient(companyId, employeeId))
        {
            var upload = await selfClient.PostAsync(
                $"/api/companies/{companyId}/employees/me/profile-photo",
                BuildPngUpload("submitted.png"));
            Assert.Equal(System.Net.HttpStatusCode.OK, upload.StatusCode);
        }

        using var managerClient = await ManagerClient(companyId);
        var task = await FindProfilePhotoReviewTaskAsync(managerClient, companyId);

        var completeResp = await managerClient.PostAsync(
            $"/api/companies/{companyId}/tasks/{task.Id}/complete",
            Json(new { }));
        Assert.False(completeResp.IsSuccessStatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        var pendingRows = await db.PendingProfilePhotos.Where(p => p.EmployeeId == employeeId).ToListAsync();
        Assert.Single(pendingRows);

        var liveRows = await db.EmployeeProfilePhotos.Where(p => p.EmployeeId == employeeId).ToListAsync();
        Assert.Empty(liveRows);

        var retryResp = await managerClient.PostAsync(
            $"/api/companies/{companyId}/tasks/{task.Id}/complete",
            Json(new { outcomeDecision = "Approve" }));
        Assert.True(retryResp.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Completing_Review_Task_Via_Generic_Endpoint_With_Invalid_OutcomeDecision_Fails_And_Leaves_Pending_Untouched()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await SeedManagerAsync(companyId);

        using (var selfClient = await SelfClient(companyId, employeeId))
        {
            var upload = await selfClient.PostAsync(
                $"/api/companies/{companyId}/employees/me/profile-photo",
                BuildPngUpload("submitted.png"));
            Assert.Equal(System.Net.HttpStatusCode.OK, upload.StatusCode);
        }

        using var managerClient = await ManagerClient(companyId);
        var task = await FindProfilePhotoReviewTaskAsync(managerClient, companyId);

        var completeResp = await managerClient.PostAsync(
            $"/api/companies/{companyId}/tasks/{task.Id}/complete",
            Json(new { outcomeDecision = "Maybe" }));
        Assert.False(completeResp.IsSuccessStatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        var pendingRows = await db.PendingProfilePhotos.Where(p => p.EmployeeId == employeeId).ToListAsync();
        Assert.Single(pendingRows);
    }

    [Fact]
    public async Task Completing_Review_Task_Via_Generic_Endpoint_With_Reject_Removes_Pending_Without_Creating_Live_Photo()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await SeedManagerAsync(companyId);

        using (var selfClient = await SelfClient(companyId, employeeId))
        {
            var upload = await selfClient.PostAsync(
                $"/api/companies/{companyId}/employees/me/profile-photo",
                BuildPngUpload("submitted.png"));
            Assert.Equal(System.Net.HttpStatusCode.OK, upload.StatusCode);
        }

        using var managerClient = await ManagerClient(companyId);
        var task = await FindProfilePhotoReviewTaskAsync(managerClient, companyId);

        var completeResp = await managerClient.PostAsync(
            $"/api/companies/{companyId}/tasks/{task.Id}/complete",
            Json(new { outcomeDecision = "Reject", outcomeReason = "Blurry" }));
        Assert.True(completeResp.IsSuccessStatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        Assert.Empty(await db.PendingProfilePhotos.Where(p => p.EmployeeId == employeeId).ToListAsync());
        Assert.Empty(await db.EmployeeProfilePhotos.Where(p => p.EmployeeId == employeeId).ToListAsync());
    }


    private async Task SeedManagerAsync(Guid companyId)
    {
        await TestRoleSeeder.AssignRoleAsync(_factory, ManagerUser, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, ManagerUser, SystemRoles.Employee);
    }

    private async Task<HttpClient> SelfClient(Guid companyId, Guid employeeId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);
        return client;
    }

    private async Task<HttpClient> ManagerClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ManagerUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ManagerUser, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static async Task<UnassignedTaskItem> FindProfilePhotoReviewTaskAsync(
        HttpClient managerClient, Guid companyId)
    {
        var response = await managerClient.GetAsync($"/api/companies/{companyId}/tasks/unassigned");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<UnassignedTasksPayload>();

        return Assert.Single(
            payload!.Items,
            t => t.Source == "Document" && t.ActionType == "Review");
    }

    private static StringContent Json(object payload) =>
        new(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static MultipartFormDataContent BuildPngUpload(string fileName = "avatar.png") =>
        BuildUpload(BuildPngBytes(400, 300), "image/png", fileName);

    private static MultipartFormDataContent BuildUpload(byte[] bytes, string contentType, string fileName)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        form.Add(fileContent, "File", fileName);
        return form;
    }

    private static byte[] BuildPngBytes(int width, int height)
    {
        var bytes = new List<byte>();
        bytes.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x0D });
        bytes.AddRange("IHDR"u8.ToArray());
        bytes.AddRange(BigEndianUInt32(width));
        bytes.AddRange(BigEndianUInt32(height));
        bytes.AddRange(new byte[] { 0x08, 0x06, 0x00, 0x00, 0x00 });
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });
        return [.. bytes];
    }

    private static byte[] BigEndianUInt32(int value) =>
    [
        (byte)((value >> 24) & 0xFF),
        (byte)((value >> 16) & 0xFF),
        (byte)((value >> 8) & 0xFF),
        (byte)(value & 0xFF),
    ];

    private sealed record UnassignedTasksPayload(IReadOnlyList<UnassignedTaskItem> Items);
    private sealed record UnassignedTaskItem(
        Guid Id, Guid CompanyId, string Title, string? Description, string Status, string Priority,
        string Source, string ActionType, DateOnly? DueDate, Guid? SourceEntityId, Guid CreatedBy,
        DateTimeOffset CreatedAt);
}
