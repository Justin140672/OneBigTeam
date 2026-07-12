using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class UploadMyProfilePhotoEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public UploadMyProfilePhotoEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/me/profile-photo",
            BuildPngUpload());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_Forbidden_When_Company_Claim_Mismatches()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString()); // different company

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/me/profile-photo",
            BuildPngUpload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Self_Upload_Returns_Ok_With_Populated_Response()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = SelfClient(companyId, employeeId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/me/profile-photo",
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

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        var pending = await db.PendingProfilePhotos.SingleAsync(p => p.EmployeeId == employeeId);
        Assert.Equal(payload.Id, pending.Id);
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_File_Too_Large()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = SelfClient(companyId, employeeId);

        var oversized = new byte[6 * 1024 * 1024]; // exceeds the default 5 MB limit

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/me/profile-photo",
            BuildUpload(oversized, "image/png", "big.png"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_ContentType_Not_Allowed()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = SelfClient(companyId, employeeId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/me/profile-photo",
            BuildUpload(BuildPngBytes(400, 300), "text/plain", "avatar.png"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_Magic_Bytes_Do_Not_Match_Declared_Type()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = SelfClient(companyId, employeeId);

        // Extension/content type claim PNG, but the bytes are not a PNG (spoofed/renamed file).
        var spoofed = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/me/profile-photo",
            BuildUpload(spoofed, "image/png", "avatar.png"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_Dimensions_Below_Minimum()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = SelfClient(companyId, employeeId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/me/profile-photo",
            BuildUpload(BuildPngBytes(10, 10), "image/png", "tiny.png"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns_UnprocessableEntity_When_Dimensions_Above_Maximum()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = SelfClient(companyId, employeeId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/me/profile-photo",
            BuildUpload(BuildPngBytes(5000, 5000), "image/png", "huge.png"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_ReUpload_Replaces_Existing_Pending_Only_One_Row_Exists()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = SelfClient(companyId, employeeId);

        var first = await client.PostAsync(
            $"/api/companies/{companyId}/employees/me/profile-photo",
            BuildUpload(BuildPngBytes(400, 300), "image/png", "first.png"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstPayload = await first.Content.ReadFromJsonAsync<ProfilePhotoPayload>();

        var second = await client.PostAsync(
            $"/api/companies/{companyId}/employees/me/profile-photo",
            BuildUpload(BuildPngBytes(500, 500), "image/png", "second.png"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondPayload = await second.Content.ReadFromJsonAsync<ProfilePhotoPayload>();

        Assert.Equal(firstPayload!.Id, secondPayload!.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        var rows = await db.PendingProfilePhotos
            .Where(p => p.EmployeeId == employeeId)
            .ToListAsync();

        Assert.Single(rows);
        Assert.Equal("second.png", rows[0].FileName);
    }

    [Fact]
    public async Task Post_Creates_Pending_Row_Without_Touching_Existing_Live_Photo()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
            db.EmployeeProfilePhotos.Add(EmployeeProfilePhoto.Create(
                Guid.NewGuid(), companyId, employeeId,
                "existing-live.png", 1234, "image/png", "existing/storage/key",
                employeeId, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        using var client = SelfClient(companyId, employeeId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/employees/me/profile-photo",
            BuildPngUpload("new-pending.png"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        var livePhoto = await db2.EmployeeProfilePhotos.SingleAsync(p => p.EmployeeId == employeeId);
        Assert.Equal("existing-live.png", livePhoto.FileName);

        var pendingPhoto = await db2.PendingProfilePhotos.SingleAsync(p => p.EmployeeId == employeeId);
        Assert.Equal("new-pending.png", pendingPhoto.FileName);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private HttpClient SelfClient(Guid companyId, Guid employeeId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
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
