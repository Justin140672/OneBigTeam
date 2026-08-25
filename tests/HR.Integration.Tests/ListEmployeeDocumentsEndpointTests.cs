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
        // DOC-01: an unrelated employee is no longer authorized to list Sarah's documents (see
        // DocumentsResourceAuthorizationTests for the full self/manager/HR-admin/peer matrix), so
        // this "does the handler actually return the seeded rows" check now uses an HR
        // administrator caller, which is unconditionally in-scope.
        using var client = await AdminClient(AcmeCompanyId);
        var response     = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{SarahEmployeeId}/documents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload      = await response.Content.ReadFromJsonAsync<DocsPayload>();
        Assert.Equal(2, payload!.Items.Count);
    }

    [Fact]
    public async Task Returns_Empty_For_Employee_With_No_Documents()
    {
        using var client     = await AdminClient(AcmeCompanyId);
        var unknownEmployeeId = Guid.NewGuid();
        var response         = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{unknownEmployeeId}/documents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload          = await response.Content.ReadFromJsonAsync<DocsPayload>();
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Returns_Forbidden_When_CompanyId_Does_Not_Match_Employee()
    {
        // DOC-01: previously this leaked a 200 with an empty list (data-isolation-by-filter);
        // now the resource authorizer denies the caller before the handler ever runs, since a
        // plain employee is neither self, HR administrator, nor a manager of SarahEmployeeId —
        // see DocumentsResourceAuthorizationTests for the equivalent cross-company matrix.
        var otherCompanyId = Guid.NewGuid();
        using var client   = await AuthenticatedClient(otherCompanyId);
        var response       = await client.GetAsync(
            $"/api/companies/{otherCompanyId}/employees/{SarahEmployeeId}/documents");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var userId = Guid.NewGuid();
        TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee).GetAwaiter().GetResult();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee, companyId);
        return client;
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var userId = Guid.NewGuid();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private sealed record DocsPayload(IReadOnlyList<DocItem> Items);
    private sealed record DocItem(Guid EmployeeDocumentId, string Title, string DocumentTypeName);
}
