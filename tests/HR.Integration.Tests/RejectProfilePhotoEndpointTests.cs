using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class RejectProfilePhotoEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ManagerUser = Guid.Parse("bb100001-0000-0000-0000-000000000001");

    public RejectProfilePhotoEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Post_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/profile-photo/pending/reject",
            EmptyJson());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_Forbidden_When_Company_Claim_Mismatches()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ManagerUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo/pending/reject",
            new { companyId, employeeId, rejectionReason = "Blurry" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_Forbidden_When_Caller_Lacks_EmployeeManage()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo/pending/reject",
            ValidReasonJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_Forbidden_When_Caller_Is_The_Employee_Themself_Without_EmployeeManage()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee, companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo/pending/reject",
            ValidReasonJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_NotFound_When_No_Pending_Photo_Exists()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await ManagerClient(companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo/pending/reject",
            ValidReasonJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_NotFound_When_EmployeeId_Belongs_To_Different_Company()
    {
        var companyB   = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using (var selfClientB = await SelfClient(companyB, employeeId))
        {
            var upload = await selfClientB.PostAsync(
                $"/api/companies/{companyB}/employees/me/profile-photo",
                BuildPngUpload("company-b.png"));
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        // An HR caller genuinely belonging to Company A (their own claim matches the route) tries
        // to reject the employeeId that actually belongs to Company B — must 404, never leak,
        // and must not touch Company B's pending photo.
        var companyA = Guid.NewGuid();
        using var clientA = await ManagerClient(companyA);

        var response = await clientA.PostAsync(
            $"/api/companies/{companyA}/employees/{employeeId}/profile-photo/pending/reject",
            ValidReasonJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        var pendingRows = await db.PendingProfilePhotos.Where(p => p.EmployeeId == employeeId).ToListAsync();
        Assert.Single(pendingRows);
        Assert.Equal(companyB, pendingRows[0].CompanyId);
    }

    [Fact]
    public async Task Post_Reject_Returns_Ok_Removes_Pending_And_Does_Not_Create_Live_Photo()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using (var selfClient = await SelfClient(companyId, employeeId))
        {
            var upload = await selfClient.PostAsync(
                $"/api/companies/{companyId}/employees/me/profile-photo",
                BuildPngUpload("submitted.png"));
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        using var client = await ManagerClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo/pending/reject",
            new { companyId, employeeId, rejectionReason = "Photo does not meet the dress code policy." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<RejectedProfilePhotoPayload>();
        Assert.NotNull(payload);
        Assert.Equal(employeeId, payload!.EmployeeId);
        Assert.Equal("Photo does not meet the dress code policy.", payload.RejectionReason);
        Assert.Equal(ManagerUser, payload.ReviewedBy);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        var pendingRows = await db.PendingProfilePhotos.Where(p => p.EmployeeId == employeeId).ToListAsync();
        Assert.Empty(pendingRows);

        var liveRows = await db.EmployeeProfilePhotos.Where(p => p.EmployeeId == employeeId).ToListAsync();
        Assert.Empty(liveRows);
    }

    [Fact]
    public async Task Post_Reject_Without_Reason_Fails_Validation()
    {
        // Ticket requirement: "Rejection must require a clear reason" — a rejection with no reason
        // must not be accepted, so the employee always knows what to fix before resubmitting.
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using (var selfClient = await SelfClient(companyId, employeeId))
        {
            var upload = await selfClient.PostAsync(
                $"/api/companies/{companyId}/employees/me/profile-photo",
                BuildPngUpload("submitted.png"));
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        using var client = await ManagerClient(companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo/pending/reject",
            EmptyJson());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        // The pending submission must remain intact — a failed/rejected rejection request must not
        // silently clear it.
        var pendingRows = await db.PendingProfilePhotos.Where(p => p.EmployeeId == employeeId).ToListAsync();
        Assert.Single(pendingRows);
    }

    [Fact]
    public async Task Post_Reject_With_Whitespace_Only_Reason_Fails_Validation()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using (var selfClient = await SelfClient(companyId, employeeId))
        {
            var upload = await selfClient.PostAsync(
                $"/api/companies/{companyId}/employees/me/profile-photo",
                BuildPngUpload("submitted.png"));
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        using var client = await ManagerClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo/pending/reject",
            new { companyId, employeeId, rejectionReason = "   " });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        var pendingRows = await db.PendingProfilePhotos.Where(p => p.EmployeeId == employeeId).ToListAsync();
        Assert.Single(pendingRows);
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

    private static StringContent EmptyJson() =>
        new("{}", Encoding.UTF8, "application/json");

    private static StringContent ValidReasonJson() =>
        new("""{"rejectionReason":"Blurry"}""", Encoding.UTF8, "application/json");

    private static MultipartFormDataContent BuildPngUpload(string fileName = "avatar.png") =>
        BuildUpload(BuildPngBytes(400, 300), "image/png", fileName);

    private static MultipartFormDataContent BuildUpload(byte[] bytes, string contentType, string fileName)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
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

    private sealed record RejectedProfilePhotoPayload(
        Guid PendingProfilePhotoId,
        Guid EmployeeId,
        string? RejectionReason,
        Guid ReviewedBy,
        DateTimeOffset ReviewedAt);
}
