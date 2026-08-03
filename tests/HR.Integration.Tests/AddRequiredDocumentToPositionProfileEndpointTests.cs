using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class AddRequiredDocumentToPositionProfileEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000009");

    public AddRequiredDocumentToPositionProfileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Post_RequiredDocuments_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/position-profiles/{Guid.NewGuid()}/required-documents",
            new { documentTypeId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_RequiredDocuments_Returns_Created_With_Correct_Payload()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Software Engineer");
        var documentTypeId = await CreateDocumentTypeAsync(client, companyId, "Passport");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents",
            new
            {
                companyId,
                positionProfileId = profileId,
                documentTypeId,
                isMandatory = true,
                dueDaysAfterStart = 30,
                requiresExpiryDate = true
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<RequiredDocumentPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(profileId, payload.PositionProfileId);
        Assert.Equal(documentTypeId, payload.DocumentTypeId);
        Assert.True(payload.IsMandatory);
        Assert.Equal(30, payload.DueDaysAfterStart);
        Assert.True(payload.RequiresExpiryDate);
    }

    [Fact]
    public async Task Post_RequiredDocuments_Returns_Conflict_For_Duplicate_DocumentType()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "HR Manager");
        var documentTypeId = await CreateDocumentTypeAsync(client, companyId, "Driving Licence");

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents",
            new { companyId, positionProfileId = profileId, documentTypeId, isMandatory = true });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents",
            new { companyId, positionProfileId = profileId, documentTypeId, isMandatory = false });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_RequiredDocuments_Returns_NotFound_For_Unknown_PositionProfile()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var documentTypeId = await CreateDocumentTypeAsync(client, companyId, "Contract");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{Guid.NewGuid()}/required-documents",
            new { companyId, positionProfileId = Guid.NewGuid(), documentTypeId, isMandatory = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_RequiredDocuments_Returns_NotFound_For_Unknown_DocumentType()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Finance Manager");

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents",
            new { companyId, positionProfileId = profileId, documentTypeId = Guid.NewGuid(), isMandatory = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_RequiredDocuments_Allows_Same_DocumentType_On_Different_Profile()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileAId = await CreatePositionProfileAsync(client, companyId, "Developer");
        var profileBId = await CreatePositionProfileAsync(client, companyId, "Designer");
        var documentTypeId = await CreateDocumentTypeAsync(client, companyId, "Right To Work");

        var responseA = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileAId}/required-documents",
            new { companyId, positionProfileId = profileAId, documentTypeId, isMandatory = true });
        responseA.EnsureSuccessStatusCode();

        var responseB = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileBId}/required-documents",
            new { companyId, positionProfileId = profileBId, documentTypeId, isMandatory = true });

        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
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
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
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
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private sealed record IdPayload(Guid Id);

    private sealed record RequiredDocumentPayload(
        Guid Id,
        Guid PositionProfileId,
        Guid DocumentTypeId,
        bool IsMandatory,
        int? DueDaysAfterStart,
        bool RequiresExpiryDate);
}
