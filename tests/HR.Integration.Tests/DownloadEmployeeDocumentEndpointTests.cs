using System.Net;
using HR.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HR.Integration.Tests;

public class DownloadEmployeeDocumentEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly Guid AcmeCompanyId      = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahEmployeeId    = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahContractDocId = Guid.Parse("70000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = factory.CreateClient();
        var response     = await client.GetAsync(DownloadUrl(AcmeCompanyId, SarahEmployeeId, SarahContractDocId));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Redirect_For_Seeded_Document()
    {
        using var client = NoRedirectClient(AcmeCompanyId);
        var response     = await client.GetAsync(DownloadUrl(AcmeCompanyId, SarahEmployeeId, SarahContractDocId));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Document()
    {
        using var client = AuthenticatedClient(AcmeCompanyId);
        var response     = await client.GetAsync(DownloadUrl(AcmeCompanyId, SarahEmployeeId, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_When_EmployeeId_Does_Not_Match()
    {
        using var client = AuthenticatedClient(AcmeCompanyId);
        var response     = await client.GetAsync(DownloadUrl(AcmeCompanyId, Guid.NewGuid(), SarahContractDocId));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string DownloadUrl(Guid companyId, Guid employeeId, Guid docId) =>
        $"/api/companies/{companyId}/employees/{employeeId}/documents/{docId}/download";

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private HttpClient NoRedirectClient(Guid companyId)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }
}
