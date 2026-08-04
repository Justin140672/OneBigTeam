using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.SharedKernel;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListDocumentTypesEndpointTests(ApiWebApplicationFactory factory)
{
    private static readonly Guid AcmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client   = factory.CreateClient();
        var response       = await client.GetAsync($"/api/companies/{AcmeCompanyId}/document-types");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Six_Active_Types_For_Acme()
    {
        using var client   = await AuthenticatedClient(AcmeCompanyId);
        var response       = await client.GetAsync($"/api/companies/{AcmeCompanyId}/document-types");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload        = await response.Content.ReadFromJsonAsync<DocTypesPayload>();
        Assert.Equal(6, payload!.Items.Count);
    }

    [Fact]
    public async Task Returns_Empty_For_Unknown_Company()
    {
        var unknownId      = Guid.NewGuid();
        using var client   = await AuthenticatedClient(unknownId);
        var response       = await client.GetAsync($"/api/companies/{unknownId}/document-types");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload        = await response.Content.ReadFromJsonAsync<DocTypesPayload>();
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Returns_Empty_When_EmployeeUploadOnly_And_None_Configured()
    {
        using var client   = await AuthenticatedClient(AcmeCompanyId);
        var response       = await client.GetAsync(
            $"/api/companies/{AcmeCompanyId}/document-types?employeeUploadOnly=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload        = await response.Content.ReadFromJsonAsync<DocTypesPayload>();
        Assert.Empty(payload!.Items);
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

    private sealed record DocTypesPayload(IReadOnlyList<DocTypeItem> Items);
    private sealed record DocTypeItem(Guid Id, string Name, bool IsActive, bool AllowEmployeeUpload);
}
