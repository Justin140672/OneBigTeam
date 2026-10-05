using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetUnassignedTasksEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser       = Guid.Parse("11100005-0000-0000-0000-000000000001");
    private static readonly Guid SeededCompanyId = Guid.Parse("11100005-0000-0000-0000-0000000000c0");

    public GetUnassignedTasksEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator, SeededCompanyId);
        }).GetAwaiter().GetResult();
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

    [Fact]
    public async Task Returns_HR_Owned_Tasks_Flagged_As_Assigned_To_HR_And_Plain_Unassigned_Tasks_Unflagged()
    {
        using var client = await AdminClient();
        var hrTitle      = $"HrOwned-{Guid.NewGuid():N}";
        var plainTitle   = $"Plain-{Guid.NewGuid():N}";

        using (var scope = _factory.Services.CreateScope())
        {
            var hrTaskCreator = scope.ServiceProvider.GetRequiredService<IHrTaskCreator>();
            await hrTaskCreator.CreateForHrAsync(
                SeededCompanyId, Guid.NewGuid(), hrTitle, null, TaskPriority.High, TaskSource.Onboarding,
                TaskActionType.Complete, null, Guid.NewGuid(), CancellationToken.None);
        }
        await TaskSeeder.SeedAsync(_factory, SeededCompanyId, plainTitle);

        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/unassigned");
        var payload  = await response.Content.ReadFromJsonAsync<UnassignedPayload>();

        Assert.True(Assert.Single(payload!.Items, t => t.Title == hrTitle).AssignedToHr);
        Assert.False(Assert.Single(payload.Items, t => t.Title == plainTitle).AssignedToHr);
    }

    [Fact]
    public async Task HR_Owned_Task_Is_Not_Visible_To_Another_Company()
    {
        using var client = await AdminClient();
        var hrTitle      = $"HrOwnedOtherCompany-{Guid.NewGuid():N}";

        using (var scope = _factory.Services.CreateScope())
        {
            var hrTaskCreator = scope.ServiceProvider.GetRequiredService<IHrTaskCreator>();
            await hrTaskCreator.CreateForHrAsync(
                Guid.NewGuid(), Guid.NewGuid(), hrTitle, null, TaskPriority.High, TaskSource.Onboarding,
                TaskActionType.Complete, null, Guid.NewGuid(), CancellationToken.None);
        }

        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/unassigned");
        var payload  = await response.Content.ReadFromJsonAsync<UnassignedPayload>();

        Assert.DoesNotContain(payload!.Items, t => t.Title == hrTitle);
    }


    private async Task<HttpClient> AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, SeededCompanyId);
        return client;
    }

    private sealed record UnassignedPayload(IReadOnlyList<UnassignedItem> Items);
    private sealed record UnassignedItem(Guid Id, string Title, string? Source, Guid? SourceEntityId, bool AssignedToHr = false);
}
