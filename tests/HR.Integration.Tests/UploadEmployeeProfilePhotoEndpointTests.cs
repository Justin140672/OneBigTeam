using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UploadEmployeeProfilePhotoEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ManagerUser = Guid.Parse("dd000001-0000-0000-0000-000000000001");

    public UploadEmployeeProfilePhotoEndpointTests(ApiWebApplicationFactory factory)
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
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/profile-photo",
            BuildPngUpload());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_Forbidden_When_Company_Claim_Mismatches()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString()); // different company

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo",
            BuildPngUpload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_Forbidden_When_Caller_Is_Neither_Employee_Nor_Manager()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var callerId   = Guid.NewGuid(); // not the employee, no employee:manage grant
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, callerId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, callerId, companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo",
            BuildPngUpload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_Forbidden_For_Self_Upload_Without_Employee_Manage_Role()
    {
        // This endpoint is HR-only: an employee uploading their own photo without the
        // employee:manage role is forbidden. Self-service uploads go through
        // UploadMyProfilePhoto instead (see UploadMyProfilePhotoEndpointTests).
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await SelfClient(companyId, employeeId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo",
            BuildPngUpload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Manager_Upload_Returns_Ok_With_Populated_Response()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await ManagerClient(companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo",
            BuildPngUpload());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ProfilePhotoPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId,  payload.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal("avatar.png", payload.FileName);
        Assert.Equal("image/png", payload.ContentType);
        Assert.False(string.IsNullOrWhiteSpace(payload.DownloadUrl));
    }

    [Fact]
    public async Task Post_Manager_Upload_To_Different_Employee_Returns_Ok()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await ManagerClient(companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo",
            BuildPngUpload());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ProfilePhotoPayload>();
        Assert.NotNull(payload);
        Assert.Equal(employeeId, payload!.EmployeeId);
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_File_Too_Large()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await ManagerClient(companyId);

        var oversized = new byte[6 * 1024 * 1024]; // exceeds the default 5 MB limit

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo",
            BuildUpload(oversized, "image/png", "big.png"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_ContentType_Not_Allowed()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await ManagerClient(companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo",
            BuildUpload(BuildPngBytes(400, 300), "text/plain", "avatar.png"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_Magic_Bytes_Do_Not_Match_Declared_Type()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await ManagerClient(companyId);

        // Extension/content type claim PNG, but the bytes are not a PNG (spoofed/renamed file).
        var spoofed = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo",
            BuildUpload(spoofed, "image/png", "avatar.png"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_Dimensions_Below_Minimum()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await ManagerClient(companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo",
            BuildUpload(BuildPngBytes(10, 10), "image/png", "tiny.png"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_Dimensions_Above_Maximum()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await ManagerClient(companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo",
            BuildUpload(BuildPngBytes(5000, 5000), "image/png", "huge.png"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_ReUpload_Replaces_Existing_Photo_Only_One_Row_Exists()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await ManagerClient(companyId);

        var first = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo",
            BuildUpload(BuildPngBytes(400, 300), "image/png", "first.png"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstPayload = await first.Content.ReadFromJsonAsync<ProfilePhotoPayload>();

        var second = await client.PostAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile-photo",
            BuildUpload(BuildPngBytes(500, 500), "image/png", "second.png"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondPayload = await second.Content.ReadFromJsonAsync<ProfilePhotoPayload>();

        Assert.Equal(firstPayload!.Id, secondPayload!.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        var rows = await db.EmployeeProfilePhotos
            .Where(p => p.EmployeeId == employeeId)
            .ToListAsync();

        Assert.Single(rows);
        Assert.Equal("second.png", rows[0].FileName);
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

    private sealed record ProfilePhotoPayload(
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
