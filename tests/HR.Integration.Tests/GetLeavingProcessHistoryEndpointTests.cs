using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetLeavingProcessHistoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("ffffffff-3100-0000-0000-000000000001");
    private static readonly Guid User2 = new("ffffffff-3100-0000-0000-000000000002");
    private static readonly Guid User3 = new("ffffffff-3100-0000-0000-000000000003");
    private static readonly Guid Manager = new("ffffffff-3100-0000-0000-000000000004");
    private static readonly Guid Employee = new("ffffffff-3100-0000-0000-000000000005");

    public GetLeavingProcessHistoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, Manager, SystemRoles.Manager);
            await TestRoleSeeder.AssignRoleAsync(factory, Employee, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    // Relative to "today" rather than hardcoded literals — see StartLeavingProcessEndpointTests'
    // identical fields for why a fixed near-term literal eventually becomes "backdated".
    private static readonly DateOnly LeavingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
    private static readonly DateOnly LastWorkingDay = LeavingDate.AddDays(-1);

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "History", "Test", $"history-test.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static async Task StartLeavingProcessAsync(
        HttpClient client,
        Guid companyId,
        Guid employeeId,
        string reason = "Resignation",
        string? notes = null)
    {
        var body = new
        {
            companyId,
            employeeId,
            resignationReceivedDate = LeavingDate.AddDays(-30).ToString("yyyy-MM-dd"),
            leavingDate = LeavingDate.ToString("yyyy-MM-dd"),
            lastWorkingDay = LastWorkingDay.ToString("yyyy-MM-dd"),
            leavingReason = reason,
            notes
        };

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            body);
        response.EnsureSuccessStatusCode();
    }

    private static async Task CancelLeavingProcessAsync(
        HttpClient client,
        Guid companyId,
        Guid employeeId,
        string cancellationReason)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process/cancel",
            new { cancellationReason });
        response.EnsureSuccessStatusCode();
    }

    private static async Task AmendLeavingProcessAsync(
        HttpClient client,
        Guid companyId,
        Guid employeeId,
        DateOnly newLeavingDate,
        DateOnly newLastWorkingDay,
        string newReason = "Resignation",
        string? notes = null)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                leavingDate = newLeavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = newLastWorkingDay.ToString("yyyy-MM-dd"),
                leavingReason = newReason,
                expectedVersion = 1,
                notes
            });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Get_LeavingProcessHistory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/leaving-process-history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeavingProcessHistory_Returns_Empty_List_When_No_Processes_Exist()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<GetLeavingProcessHistoryResponse>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_LeavingProcessHistory_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/leaving-process-history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeavingProcessHistory_Returns_Single_InProgress_Process()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<GetLeavingProcessHistoryResponse>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);

        var item = payload.Items[0];
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal("InProgress", item.Status);
        Assert.Equal(LeavingDate.AddDays(-30), item.ResignationReceivedDate);
        Assert.Equal(LeavingDate, item.LeavingDate);
        Assert.Equal(LastWorkingDay, item.LastWorkingDay);
        Assert.Equal("Resignation", item.LeavingReason);
        Assert.Null(item.Notes);
        Assert.Null(item.ReplacementManagerName);
        Assert.Null(item.CancelledAt);
        Assert.Null(item.CancellationReason);
        Assert.True(item.StartedAt > DateTimeOffset.MinValue);
        Assert.True(item.UpdatedAt >= item.StartedAt);
    }

    [Fact]
    public async Task Get_LeavingProcessHistory_Returns_Multiple_Processes_In_Reverse_Chronological_Order()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);

        // Create first process
        await StartLeavingProcessAsync(client, companyId, employeeId, "Resignation");
        await Task.Delay(100); // Small delay to ensure different timestamps

        // Amend the process to create an updated timestamp
        var earlierLeavingDate = LeavingDate.AddDays(5);
        var earlierLastWorkingDay = earlierLeavingDate.AddDays(-1);
        await AmendLeavingProcessAsync(client, companyId, employeeId, earlierLeavingDate, earlierLastWorkingDay);

        // Cancel the process
        await CancelLeavingProcessAsync(client, companyId, employeeId, "Changed my mind");

        // Create a second process
        await StartLeavingProcessAsync(client, companyId, employeeId, "Retirement");

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<GetLeavingProcessHistoryResponse>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);

        // Most recent (second process) should come first
        var mostRecent = payload.Items[0];
        Assert.Equal("InProgress", mostRecent.Status);
        Assert.Equal("Retirement", mostRecent.LeavingReason);
        Assert.Null(mostRecent.CancelledAt);

        // Older (first process, now cancelled) should come second
        var older = payload.Items[1];
        Assert.Equal("Cancelled", older.Status);
        Assert.Equal("Resignation", older.LeavingReason);
        Assert.NotNull(older.CancelledAt);
        Assert.Equal("Changed my mind", older.CancellationReason);

        // Verify reverse-chronological ordering by StartedAt
        Assert.True(mostRecent.StartedAt >= older.StartedAt,
            "Most recent process should have StartedAt >= older process");
    }

    [Fact]
    public async Task Get_LeavingProcessHistory_Returns_Cancelled_Process_With_CancellationReason()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User3, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        const string cancellationReason = "Employee decided to stay with the company";
        await CancelLeavingProcessAsync(client, companyId, employeeId, cancellationReason);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<GetLeavingProcessHistoryResponse>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);

        var item = payload.Items[0];
        Assert.Equal("Cancelled", item.Status);
        Assert.NotNull(item.CancelledAt);
        Assert.Equal(cancellationReason, item.CancellationReason);
    }

    [Fact]
    public async Task Get_LeavingProcessHistory_Returns_Notes_When_Present()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);

        const string notes = "Relocating to another country";
        await StartLeavingProcessAsync(client, companyId, employeeId, "Other", notes);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<GetLeavingProcessHistoryResponse>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);

        var item = payload.Items[0];
        Assert.Equal(notes, item.Notes);
    }

    [Fact]
    public async Task Get_LeavingProcessHistory_Resolves_ReplacementManagerName()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        var replacementManagerId = await CreateEmployeeAsync(client, companyId);

        // Start a leaving process with a replacement manager
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var startResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = LeavingDate.AddDays(-30).ToString("yyyy-MM-dd"),
                leavingDate = LeavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = LastWorkingDay.ToString("yyyy-MM-dd"),
                leavingReason = "Resignation",
                replacementManagerEmployeeId = replacementManagerId
            });
        startResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<GetLeavingProcessHistoryResponse>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);

        var item = payload.Items[0];
        Assert.NotNull(item.ReplacementManagerName);
        Assert.NotEmpty(item.ReplacementManagerName);
        // Verify it contains the expected parts (First + Last name)
        Assert.Contains(" ", item.ReplacementManagerName);
    }

    [Fact]
    public async Task Get_LeavingProcessHistory_Returns_Forbidden_For_Manager()
    {
        using var hrAdminClient = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        hrAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        hrAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);
        await StartLeavingProcessAsync(hrAdminClient, companyId, employeeId);

        // A different user with Manager role only
        using var managerClient = _factory.CreateClient();
        var managerId = Guid.NewGuid();
        managerClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, managerId.ToString());
        managerClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager, companyId);

        var response = await managerClient.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process-history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeavingProcessHistory_Returns_Forbidden_For_Employee()
    {
        using var hrAdminClient = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        hrAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        hrAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);
        await StartLeavingProcessAsync(hrAdminClient, companyId, employeeId);

        // A different user with Employee role only
        using var employeeClient = _factory.CreateClient();
        var otherEmployeeId = Guid.NewGuid();
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, otherEmployeeId.ToString());
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, otherEmployeeId, SystemRoles.Employee, companyId);

        var response = await employeeClient.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process-history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeavingProcessHistory_Returns_Forbidden_For_Recruiter()
    {
        using var hrAdminClient = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        hrAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        hrAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User3, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);
        await StartLeavingProcessAsync(hrAdminClient, companyId, employeeId);

        // A different user with Recruiter role
        using var recruiterClient = _factory.CreateClient();
        var recruiterId = Guid.NewGuid();
        recruiterClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, recruiterId.ToString());
        recruiterClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, recruiterId, SystemRoles.Recruiter, companyId);

        var response = await recruiterClient.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process-history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeavingProcessHistory_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        // Authenticated as companyId but the route targets otherCompanyId —
        // TenantRouteAuthorizationMiddleware blocks it before the handler ever runs.
        var response = await client.GetAsync(
            $"/api/companies/{otherCompanyId}/employees/{employeeId}/leaving-process-history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeavingProcessHistory_Returns_All_Fields_Mapped_Correctly()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);

        const string notes = "Test notes";
        const string reason = "Other";
        await StartLeavingProcessAsync(client, companyId, employeeId, reason, notes);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<GetLeavingProcessHistoryResponse>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);

        var item = payload.Items[0];

        // Verify all fields are present and correctly mapped
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.NotNull(item.Status);
        Assert.NotNull(item.ResignationReceivedDate);
        Assert.NotNull(item.LeavingDate);
        Assert.NotNull(item.LastWorkingDay);
        Assert.NotNull(item.LeavingReason);
        Assert.Equal(notes, item.Notes);
        Assert.True(item.StartedAt > DateTimeOffset.MinValue);
        Assert.True(item.UpdatedAt >= item.StartedAt);
        // These should be null for an InProgress process
        Assert.Null(item.CancelledAt);
        Assert.Null(item.CancellationReason);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record GetLeavingProcessHistoryResponse(IReadOnlyList<LeavingProcessHistoryItem> Items);

    private sealed record LeavingProcessHistoryItem(
        Guid Id,
        string Status,
        DateOnly ResignationReceivedDate,
        DateOnly LeavingDate,
        DateOnly LastWorkingDay,
        string LeavingReason,
        string? Notes,
        string? ReplacementManagerName,
        DateTimeOffset StartedAt,
        DateTimeOffset? CancelledAt,
        string? CancellationReason,
        DateTimeOffset? FinalisationCompletedAt,
        DateTimeOffset UpdatedAt);
}
