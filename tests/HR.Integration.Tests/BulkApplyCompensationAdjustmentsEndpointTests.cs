using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class BulkApplyCompensationAdjustmentsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("bbbbbbbb-1000-0000-0000-000000000001");
    private static readonly Guid User2 = new("bbbbbbbb-1000-0000-0000-000000000002");
    private static readonly Guid User3 = new("bbbbbbbb-1000-0000-0000-000000000003");

    public BulkApplyCompensationAdjustmentsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Post_Bulk_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/compensation/bulk", new
        {
            companyId,
            effectiveDate = "2027-01-01",
            reason = "AnnualReview",
            adjustmentMode = "PercentageIncrease",
            items = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Bulk_Applies_Adjustments_To_Multiple_Employees()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var (employeeId1, _) = await CompensationTestHelpers.CreateEmployeeWithNumberAsync(client, companyId);
        var (employeeId2, _) = await CompensationTestHelpers.CreateEmployeeWithNumberAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/compensation/bulk", new
        {
            companyId,
            effectiveDate = "2027-01-01",
            reason = "AnnualReview",
            adjustmentMode = "PercentageIncrease",
            items = new object[]
            {
                new { employeeId = employeeId1, proposedSalary = 45000m, salaryType = "Annual", currency = "GBP" },
                new { employeeId = employeeId2, proposedSalary = 47000m, salaryType = "Annual", currency = "GBP" }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<BulkResponsePayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.BulkOperationId);
        Assert.Equal(2, payload.Items.Count);
    }

    [Fact]
    public async Task Post_Bulk_Returns_Conflict_When_An_Item_Overlaps_Existing_Record()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var (employeeId, _) = await CompensationTestHelpers.CreateEmployeeWithNumberAsync(client, companyId);

        var first = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/compensation", new
        {
            companyId,
            employeeId,
            effectiveFrom = "2027-01-01",
            salaryType = "Annual",
            salary = 50000m,
            currency = "GBP",
            reason = "NewHire"
        });
        first.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/compensation/bulk", new
        {
            companyId,
            effectiveDate = "2027-01-01",
            reason = "AnnualReview",
            adjustmentMode = "PercentageIncrease",
            items = new object[]
            {
                new { employeeId, proposedSalary = 52000m, salaryType = "Annual", currency = "GBP" }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_Bulk_Returns_UnprocessableEntity_For_Empty_Items()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/compensation/bulk", new
        {
            companyId,
            effectiveDate = "2027-01-01",
            reason = "AnnualReview",
            adjustmentMode = "PercentageIncrease",
            items = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Bulk_Returns_ClientError_For_Invalid_Enum_Values()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var (employeeId, _) = await CompensationTestHelpers.CreateEmployeeWithNumberAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/compensation/bulk", new
        {
            companyId,
            effectiveDate = "2027-01-01",
            reason = "NotARealReason",
            adjustmentMode = "PercentageIncrease",
            items = new object[]
            {
                new { employeeId, proposedSalary = 45000m, salaryType = "Annual", currency = "GBP" }
            }
        });

        // A JSON string that doesn't map to any CompensationChangeReason member fails during model
        // binding/deserialization before FluentValidation even runs, so this is expected to surface as
        // a 400 (deserialization failure) rather than FluentValidation's usual 422 — either way it must
        // not succeed.
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"Expected a client error status but got {response.StatusCode}.");
    }

    private sealed record BulkResultItemPayload(Guid EmployeeId, Guid CompensationRecordId, decimal PreviousSalary, decimal NewSalary, DateOnly EffectiveFrom);
    private sealed record BulkResponsePayload(Guid BulkOperationId, IReadOnlyList<BulkResultItemPayload> Items);
}
