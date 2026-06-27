using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class RemoveRequiredDocumentFromPositionProfileEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000009");

    public RemoveRequiredDocumentFromPositionProfileEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Delete_RequiredDocument_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/position-profiles/{Guid.NewGuid()}/required-documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RequiredDocument_Returns_NoContent_And_Deactivates_Document()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Software Engineer");
        var documentTypeId = await CreateDocumentTypeAsync(client, companyId, "Passport");
        var requiredDocId = await AddRequiredDocumentAsync(client, companyId, profileId, documentTypeId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents/{requiredDocId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/position-profiles/{profileId}");
        getResponse.EnsureSuccessStatusCode();
        var profile = await getResponse.Content.ReadFromJsonAsync<ProfilePayload>();
        Assert.NotNull(profile);
        Assert.Empty(profile!.RequiredDocuments);
    }

    [Fact]
    public async Task Delete_RequiredDocument_Returns_NotFound_When_Already_Removed()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "HR Manager");
        var documentTypeId = await CreateDocumentTypeAsync(client, companyId, "Driving Licence");
        var requiredDocId = await AddRequiredDocumentAsync(client, companyId, profileId, documentTypeId);

        var first = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents/{requiredDocId}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents/{requiredDocId}");
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Delete_RequiredDocument_Returns_NotFound_For_Unknown_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Finance Manager");

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RequiredDocument_Allows_Same_DocumentType_To_Be_Re_Added_After_Removal()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Operations Lead");
        var documentTypeId = await CreateDocumentTypeAsync(client, companyId, "Right To Work");
        var requiredDocId = await AddRequiredDocumentAsync(client, companyId, profileId, documentTypeId);

        var deleteResponse = await client.DeleteAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents/{requiredDocId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var addAgainResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents",
            new { companyId, positionProfileId = profileId, documentTypeId, isMandatory = false });
        Assert.Equal(HttpStatusCode.Created, addAgainResponse.StatusCode);
    }

    private async Task<Guid> CreatePositionProfileAsync(HttpClient client, Guid companyId, string title)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, title, isManagerial = false });
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

    private async Task<Guid> AddRequiredDocumentAsync(
        HttpClient client, Guid companyId, Guid profileId, Guid documentTypeId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents",
            new { companyId, positionProfileId = profileId, documentTypeId, isMandatory = true });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private sealed record IdPayload(Guid Id);

    private sealed record RequiredDocumentItem(
        Guid Id,
        Guid DocumentTypeId,
        bool IsMandatory,
        int? DueDaysAfterStart,
        bool RequiresExpiryDate);

    private sealed record ProfilePayload(
        Guid Id,
        IReadOnlyList<RequiredDocumentItem> RequiredDocuments);
}
