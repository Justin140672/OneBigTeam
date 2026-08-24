using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class MarkProbationNotApplicableEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("eeeeeeee-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("eeeeeeee-0000-0000-0000-000000000003");
    private static readonly Guid User4 = new("eeeeeeee-0000-0000-0000-000000000004");

    public MarkProbationNotApplicableEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User4, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Post_NotApplicable_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation/employees/{employeeId}/not-applicable", new
            {
                companyId,
                employeeId
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_NotApplicable_With_Existing_Active_Record_Transitions_To_NotApplicable()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId,
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        createResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation/employees/{employeeId}/not-applicable", new
            {
                companyId,
                employeeId,
                reason = "Exempt role."
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<MarkNotApplicablePayload>();
        Assert.NotNull(payload);
        Assert.Equal("NotApplicable", payload!.Status);
        Assert.Equal("Exempt role.", payload.NotApplicableReason);
    }

    [Fact]
    public async Task Post_NotApplicable_With_No_Existing_Record_Creates_NotApplicable_Record()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation/employees/{employeeId}/not-applicable", new
            {
                companyId,
                employeeId,
                managerEmployeeId = managerId,
                startDate = "2026-06-01",
                expectedEndDate = "2026-09-01",
                reason = "Employment type exempt."
            });

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created);

        var payload = await response.Content.ReadFromJsonAsync<MarkNotApplicablePayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal("NotApplicable", payload.Status);
        Assert.Equal("Employment type exempt.", payload.NotApplicableReason);
    }

    [Fact]
    public async Task Post_NotApplicable_Returns_Conflict_When_Existing_Record_Is_Passed()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User3, SystemRoles.HrAdministrator, companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId,
            managerEmployeeId = managerId,
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ProbationRecordPayload>();

        var reviewResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = created!.Id,
            reviewType = "FinalDecision",
            dueDate = "2026-09-01"
        });
        reviewResponse.EnsureSuccessStatusCode();
        var review = await reviewResponse.Content.ReadFromJsonAsync<ReviewItem>();

        var completeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{created.Id}/reviews/{review!.Id}/complete",
            new
            {
                companyId,
                probationRecordId = created.Id,
                reviewId = review.Id,
                outcome = "Pass",
                decisionDate = "2026-09-01"
            });
        completeResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation/employees/{employeeId}/not-applicable", new
            {
                companyId,
                employeeId,
                reason = "Attempted after decision."
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_NotApplicable_With_No_Existing_Record_And_Missing_Required_Fields_Returns_BadRequest()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User4.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User4, SystemRoles.HrAdministrator, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation/employees/{employeeId}/not-applicable", new
            {
                companyId,
                employeeId,
                reason = "No manager/dates supplied."
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record ReviewItem(
        Guid Id,
        Guid ProbationRecordId,
        string ReviewType,
        DateOnly DueDate,
        string Status,
        DateTimeOffset? CompletedAt,
        Guid? CompletedByEmployeeId,
        string? Notes);

    private sealed record ProbationRecordPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        Guid ManagerEmployeeId,
        DateOnly StartDate,
        DateOnly ExpectedEndDate,
        string Status,
        string? Notes,
        DateTimeOffset CreatedAt);

    private sealed record MarkNotApplicablePayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        string Status,
        string? NotApplicableReason,
        DateTimeOffset UpdatedAt);
}
