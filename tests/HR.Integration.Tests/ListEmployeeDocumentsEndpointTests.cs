using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.SharedKernel;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListEmployeeDocumentsEndpointTests(ApiWebApplicationFactory factory)
{
    private static readonly Guid AcmeCompanyId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahEmployeeId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = factory.CreateClient();
        var response     = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{SarahEmployeeId}/documents");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Two_Seeded_Documents_For_Sarah()
    {
        using var client = AuthenticatedClient(AcmeCompanyId);
        var response     = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{SarahEmployeeId}/documents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload      = await response.Content.ReadFromJsonAsync<DocsPayload>();
        Assert.Equal(2, payload!.Items.Count);
    }

    [Fact]
    public async Task Returns_Empty_For_Employee_With_No_Documents()
    {
        using var client     = AuthenticatedClient(AcmeCompanyId);
        var unknownEmployeeId = Guid.NewGuid();
        var response         = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{unknownEmployeeId}/documents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload          = await response.Content.ReadFromJsonAsync<DocsPayload>();
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Returns_Empty_When_CompanyId_Does_Not_Match()
    {
        var otherCompanyId = Guid.NewGuid();
        using var client   = AuthenticatedClient(otherCompanyId);
        var response       = await client.GetAsync(
            $"/api/companies/{otherCompanyId}/employees/{SarahEmployeeId}/documents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload        = await response.Content.ReadFromJsonAsync<DocsPayload>();
        Assert.Empty(payload!.Items);
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var userId = Guid.NewGuid();
        TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee).GetAwaiter().GetResult();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private sealed record DocsPayload(IReadOnlyList<DocItem> Items);
    private sealed record DocItem(Guid EmployeeDocumentId, string Title, string DocumentTypeName);
}
