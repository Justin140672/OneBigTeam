using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetMyProfilePhotoEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ManagerUser = Guid.Parse("ee000001-0000-0000-0000-000000000001");

    public GetMyProfilePhotoEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/me/profile-photo");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_Ok_With_Both_Null_When_No_Photos_Exist()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = SelfClient(companyId, employeeId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/me/profile-photo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<MyProfilePhotoPayload>();
        Assert.NotNull(payload);
        Assert.Null(payload!.CurrentPhoto);
        Assert.Null(payload.PendingPhoto);
    }

    [Fact]
    public async Task Get_Returns_Ok_With_Both_Current_And_Pending_Populated()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        // HR uploads a live photo directly on behalf of the employee.
        using (var managerClient = ManagerClient(companyId))
        {
            var liveUpload = await managerClient.PostAsync(
                $"/api/companies/{companyId}/employees/{employeeId}/profile-photo",
                BuildPngUpload("live.png"));
            Assert.Equal(HttpStatusCode.OK, liveUpload.StatusCode);
        }

        using var client = SelfClient(companyId, employeeId);

        // Employee then submits a new photo, which lands in the pending queue rather than
        // replacing the live photo immediately.
        var pendingUpload = await client.PostAsync(
            $"/api/companies/{companyId}/employees/me/profile-photo",
            BuildPngUpload("pending.png"));
        Assert.Equal(HttpStatusCode.OK, pendingUpload.StatusCode);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/me/profile-photo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<MyProfilePhotoPayload>();
        Assert.NotNull(payload);

        Assert.NotNull(payload!.CurrentPhoto);
        Assert.Equal("live.png", payload.CurrentPhoto!.FileName);
        Assert.False(string.IsNullOrWhiteSpace(payload.CurrentPhoto.DownloadUrl));

        Assert.NotNull(payload.PendingPhoto);
        Assert.Equal("pending.png", payload.PendingPhoto!.FileName);
        Assert.False(string.IsNullOrWhiteSpace(payload.PendingPhoto.DownloadUrl));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private HttpClient SelfClient(Guid companyId, Guid employeeId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private HttpClient ManagerClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ManagerUser.ToString());
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

    private sealed record MyProfilePhotoPayload(CurrentPhotoPayload? CurrentPhoto, PendingPhotoPayload? PendingPhoto);

    private sealed record CurrentPhotoPayload(
        Guid Id,
        string FileName,
        long FileSize,
        string ContentType,
        string DownloadUrl,
        DateTimeOffset UpdatedAt);

    private sealed record PendingPhotoPayload(
        Guid Id,
        string FileName,
        long FileSize,
        string ContentType,
        string DownloadUrl,
        DateTimeOffset CreatedAt);
}
