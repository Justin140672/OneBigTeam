using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListRequiredDocumentsForPositionProfileEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000009");

    public ListRequiredDocumentsForPositionProfileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    [Fact]
    public async Task Get_RequiredDocuments_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/position-profiles/{Guid.NewGuid()}/required-documents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_RequiredDocuments_Returns_NotFound_For_Unknown_PositionProfile()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/position-profiles/{Guid.NewGuid()}/required-documents");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_RequiredDocuments_Returns_Empty_List_When_None_Added()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Operations Lead");

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_RequiredDocuments_Returns_Active_Documents()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Software Engineer");
        var docTypeAId = await CreateDocumentTypeAsync(client, companyId, "Passport");
        var docTypeBId = await CreateDocumentTypeAsync(client, companyId, "Right To Work");

        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeAId, isMandatory: true);
        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeBId, isMandatory: false);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
        Assert.Contains(payload.Items, i => i.DocumentTypeId == docTypeAId && i.DocumentTypeName == "Passport" && i.IsMandatory);
        Assert.Contains(payload.Items, i => i.DocumentTypeId == docTypeBId && i.DocumentTypeName == "Right To Work" && !i.IsMandatory);
    }

    [Fact]
    public async Task Get_RequiredDocuments_Excludes_Removed_Documents()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "HR Manager");
        var docTypeAId = await CreateDocumentTypeAsync(client, companyId, "Driving Licence");
        var docTypeBId = await CreateDocumentTypeAsync(client, companyId, "DBS Check");

        var docAId = await AddRequiredDocumentAsync(client, companyId, profileId, docTypeAId, isMandatory: true);
        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeBId, isMandatory: true);

        var deleteResponse = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents/{docAId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);
        Assert.Equal(docTypeBId, payload.Items[0].DocumentTypeId);
    }

    [Fact]
    public async Task Get_RequiredDocuments_Is_Scoped_To_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        using var clientA = await AuthenticatedClient(companyA);
        using var clientB = await AuthenticatedClient(companyB);

        var profileAId = await CreatePositionProfileAsync(clientA, companyA, "Developer");
        var profileBId = await CreatePositionProfileAsync(clientB, companyB, "Developer");

        var docTypeAId = await CreateDocumentTypeAsync(clientA, companyA, "Contract");
        var docTypeBId = await CreateDocumentTypeAsync(clientB, companyB, "Contract");

        await AddRequiredDocumentAsync(clientA, companyA, profileAId, docTypeAId, isMandatory: true);
        await AddRequiredDocumentAsync(clientB, companyB, profileBId, docTypeBId, isMandatory: true);

        var response = await clientA.GetAsync(
            $"/api/companies/{companyA}/position-profiles/{profileAId}/required-documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);
        Assert.Equal(docTypeAId, payload.Items[0].DocumentTypeId);
    }

    private async Task<Guid> CreatePositionProfileAsync(HttpClient client, Guid companyId, string title)
    {
        var departmentId = await CreateDepartmentAsync(client, companyId);
        var locationId = await CreateLocationAsync(client, companyId);
        var leavePolicyId = await CreateLeavePolicyAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, defaultLeavePolicyId = leavePolicyId, title });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static async Task<Guid> CreateDepartmentAsync(HttpClient client, Guid companyId, string name = "Engineering")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = $"{name} {Guid.NewGuid():N}"
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private static async Task<Guid> CreateLocationAsync(HttpClient client, Guid companyId, string name = "Head Office")
    {
        var locationTypeResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/location-types", new
        {
            companyId,
            name = $"Office Type {Guid.NewGuid():N}"
        });
        locationTypeResponse.EnsureSuccessStatusCode();
        var locationType = await locationTypeResponse.Content.ReadFromJsonAsync<IdPayload>();

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name = $"{name} {Guid.NewGuid():N}",
            locationTypeId = locationType!.Id
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private static async Task<Guid> CreateLeavePolicyAsync(HttpClient client, Guid companyId, string name = "Standard Leave")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-policies", new
        {
            companyId,
            name = $"{name} {Guid.NewGuid():N}",
            carryOverDays = 5,
            allowNegativeBalance = false
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private async Task<Guid> CreateDocumentTypeAsync(HttpClient client, Guid companyId, string name)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-types",
            new { companyId, name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task<Guid> AddRequiredDocumentAsync(
        HttpClient client, Guid companyId, Guid profileId, Guid documentTypeId, bool isMandatory)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents",
            new { companyId, positionProfileId = profileId, documentTypeId, isMandatory });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private sealed record IdPayload(Guid Id);

    private sealed record RequiredDocumentItem(
        Guid Id,
        Guid DocumentTypeId,
        string DocumentTypeName,
        bool IsMandatory,
        int? DueDaysAfterStart,
        bool RequiresExpiryDate);

    private sealed record ListPayload(IReadOnlyList<RequiredDocumentItem> Items);
}
