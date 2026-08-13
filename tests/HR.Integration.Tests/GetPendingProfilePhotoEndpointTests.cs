using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetPendingProfilePhotoEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ManagerUser = Guid.Parse("ff000001-0000-0000-0000-000000000001");

    public GetPendingProfilePhotoEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/profile-photo/pending");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_Forbidden_When_Company_Claim_Mismatches()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ManagerUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString()); // different company

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo/pending");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_Forbidden_When_Caller_Lacks_EmployeeManage()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo/pending");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_Forbidden_When_Caller_Is_The_Employee_Themself_Without_EmployeeManage()
    {
        // GetPendingProfilePhoto has no "self" bypass in the endpoint — it strictly requires
        // the employee:manage policy, even when the caller is the target employee themself.
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo/pending");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_NotFound_When_No_Pending_Photo_Exists()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await ManagerClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo/pending");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_NotFound_When_EmployeeId_Belongs_To_Different_Company()
    {
        // The employee (and their pending photo) genuinely belong to Company B.
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
        // to fetch the employeeId that actually belongs to Company B — must 404, never leak.
        var companyA = Guid.NewGuid();
        using var clientA = await ManagerClient(companyA);

        var response = await clientA.GetAsync(
            $"/api/companies/{companyA}/employees/{employeeId}/profile-photo/pending");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_Ok_With_Populated_Response()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using (var selfClient = await SelfClient(companyId, employeeId))
        {
            var upload = await selfClient.PostAsync(
                $"/api/companies/{companyId}/employees/me/profile-photo",
                BuildPngUpload("pending.png"));
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        // Uploads are scanned asynchronously via a Hangfire job (ScanUploadedFileJob) that never
        // actually runs inside this integration test — simulate a completed Clean scan directly so
        // this read test doesn't need to know about ScanStatusAccessGuard (that guard itself is
        // covered by dedicated tests).
        await MarkPendingPhotoScanCleanAsync(companyId, employeeId);

        using var client = await ManagerClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo/pending");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PendingProfilePhotoPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId,  payload.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal("pending.png", payload.FileName);
        Assert.Equal("image/png", payload.ContentType);
        Assert.False(string.IsNullOrWhiteSpace(payload.DownloadUrl));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

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

    private async Task MarkPendingPhotoScanCleanAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Documents.Persistence.DocumentsDbContext>();
        var pending = await db.PendingProfilePhotos
            .SingleAsync(p => p.CompanyId == companyId && p.EmployeeId == employeeId);
        pending.MarkScanClean(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

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

    // Builds a minimal-but-valid PNG byte stream: signature + IHDR chunk carrying the given
    // width/height at the big-endian offsets ImageUploadValidator reads (16/20).
    private static byte[] BuildPngBytes(int width, int height)
    {
        var bytes = new List<byte>();
        bytes.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }); // signature
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x0D }); // IHDR chunk data length
        bytes.AddRange("IHDR"u8.ToArray());
        bytes.AddRange(BigEndianUInt32(width));
        bytes.AddRange(BigEndianUInt32(height));
        bytes.AddRange(new byte[] { 0x08, 0x06, 0x00, 0x00, 0x00 }); // bit depth, color type, compression, filter, interlace
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // dummy CRC (not validated)
        return [.. bytes];
    }

    private static byte[] BigEndianUInt32(int value) =>
    [
        (byte)((value >> 24) & 0xFF),
        (byte)((value >> 16) & 0xFF),
        (byte)((value >> 8) & 0xFF),
        (byte)(value & 0xFF),
    ];

    private sealed record PendingProfilePhotoPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        string FileName,
        long FileSize,
        string ContentType,
        string DownloadUrl,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
