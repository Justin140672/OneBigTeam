using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UploadCompanyLogoEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000008");

    public UploadCompanyLogoEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.CompanyAdministrator, tenantId);
        return client;
    }

    [Fact]
    public async Task Post_Company_Logo_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/branding/logos/PrimaryLogo",
            new { fileName = "logo.png", contentType = "image/png", fileSizeBytes = 1024 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Company_Logo_Uploads_Primary_Logo_For_Authenticated_Request()
    {
        using var client = await AuthenticatedClient(UserId);

        var createResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Logo Test {Guid.NewGuid():N}",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", countryCode = "GB" }
            }
        });
        createResponse.EnsureSuccessStatusCode();

        var createdCompany = await createResponse.Content.ReadFromJsonAsync<CreateCompanyPayload>();
        Assert.NotNull(createdCompany);

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, createdCompany!.Id.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{createdCompany.Id}/branding/logos/PrimaryLogo",
            new { fileName = "logo.png", contentType = "image/png", fileSizeBytes = 1024 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UploadCompanyLogoPayload>();
        Assert.NotNull(payload);
        Assert.Equal(createdCompany.Id, payload!.CompanyId);
        Assert.Equal("PrimaryLogo", payload.AssetType);
        Assert.Contains("logo.png", payload.LogoUrl);
    }

    [Fact]
    public async Task Post_Company_Logo_Returns_NotFound_For_Unknown_Id()
    {
        using var client = await AuthenticatedClient(UserId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/branding/logos/PrimaryLogo",
            new { fileName = "logo.png", contentType = "image/png", fileSizeBytes = 1024 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record CreateCompanyPayload(Guid Id);

    private sealed record UploadCompanyLogoPayload(
        Guid CompanyId,
        string AssetType,
        string LogoUrl,
        DateTimeOffset UpdatedAt);
}
