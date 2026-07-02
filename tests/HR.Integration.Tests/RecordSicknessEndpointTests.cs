using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Sickness.Domain;

namespace HR.Integration.Tests;

public class RecordSicknessEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000010-0000-0000-0000-000000000001");

    public RecordSicknessEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<Guid> CreateCategory(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = $"Category-{Guid.NewGuid():N}",
            displayOrder = 1
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CategoryPayload>();
        return payload!.Id;
    }

    [Fact]
    public async Task Post_SicknessRecords_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/sickness-records",
            new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_SicknessRecords_Returns_Created_With_Record()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var categoryId = await CreateCategory(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
            new
            {
                companyId,
                employeeId,
                categoryId,
                startDate = "2026-07-01",
                startDayPart = 0, // FullDay
                notes = "Feeling unwell"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<SicknessRecordPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal(categoryId, payload.CategoryId);
        Assert.Equal(0, payload.Status); // Active
        Assert.Equal(0, payload.EvidenceStatus); // NotRequired
        Assert.Equal("Feeling unwell", payload.Notes);
    }

    [Fact]
    public async Task Post_SicknessRecords_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records",
            new
            {
                companyId,
                employeeId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                startDate = "2026-07-01",
                startDayPart = 0
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_SicknessRecords_Returns_UnprocessableEntity_When_CategoryId_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records",
            new
            {
                companyId,
                employeeId = Guid.NewGuid(),
                categoryId = Guid.Empty,
                startDate = "2026-07-01",
                startDayPart = 0
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_SicknessRecords_Returns_UnprocessableEntity_When_StartDate_Is_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records",
            new
            {
                companyId,
                employeeId = Guid.NewGuid(),
                categoryId = Guid.NewGuid()
                // startDate omitted — will deserialize as default(DateOnly)
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record CategoryPayload(Guid Id);

    private sealed record SicknessRecordPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        Guid CategoryId,
        int Status,
        string StartDate,
        int StartDayPart,
        int EvidenceStatus,
        string? Notes,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
