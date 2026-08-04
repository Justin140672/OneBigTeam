using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.SharedKernel;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetUnassignedTasksEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser       = Guid.Parse("11100005-0000-0000-0000-000000000001");
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public GetUnassignedTasksEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/unassigned");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_Without_Employee_Manage_Role()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());

        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/unassigned");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_OK_With_Empty_List_When_No_Unassigned_Tasks()
    {
        using var client = await AdminClient();

        // Seed a task WITH an assignee to ensure it does not pollute unassigned results
        var uniqueEmployee = Guid.NewGuid();
        await TaskSeeder.SeedAsync(_factory, SeededCompanyId,
            title: "Assigned task — should not appear",
            assignedEmployeeId: uniqueEmployee);

        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/unassigned");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UnassignedPayload>();
        Assert.DoesNotContain(payload!.Items, t => t.Title == "Assigned task — should not appear");
    }

    [Fact]
    public async Task Returns_Tasks_With_No_Assigned_Employee_Or_User()
    {
        using var client = await AdminClient();
        var title        = $"Unassigned-{Guid.NewGuid():N}";

        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, title: title);

        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/unassigned");
        var payload  = await response.Content.ReadFromJsonAsync<UnassignedPayload>();

        Assert.Contains(payload!.Items, t => t.Title == title);
    }

    [Fact]
    public async Task Does_Not_Return_Assigned_Tasks()
    {
        using var client     = await AdminClient();
        var assignedTitle    = $"Assigned-{Guid.NewGuid():N}";
        var unassignedTitle  = $"Unassigned-{Guid.NewGuid():N}";

        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, assignedTitle,
            assignedEmployeeId: Guid.NewGuid());
        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, unassignedTitle);

        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/unassigned");
        var payload  = await response.Content.ReadFromJsonAsync<UnassignedPayload>();

        Assert.DoesNotContain(payload!.Items, t => t.Title == assignedTitle);
        Assert.Contains(payload.Items, t => t.Title == unassignedTitle);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, SeededCompanyId);
        return client;
    }

    private sealed record UnassignedPayload(IReadOnlyList<UnassignedItem> Items);
    private sealed record UnassignedItem(Guid Id, string Title, string? Source, Guid? SourceEntityId);
}
