using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Proves GetMyOnboardingStatus is reachable by a plain, authenticated Employee — resolving the
/// employee purely from the caller's own "sub" claim — backing the "Onboarding Progress" card on
/// MyProfileOverviewTab.razor.
/// </summary>
[Collection("Integration")]
public class GetMyOnboardingStatusEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid SelfUser = new("cc00000b-0000-0000-0000-000000000001");
    private static readonly Guid OtherUser = new("cc00000b-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetMyOnboardingStatusEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, SelfUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, OtherUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_MyOnboardingStatus_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/employees/me/onboarding-status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_MyOnboardingStatus_Returns_HasPlan_False_When_No_Plan_Exists()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(SelfUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/me/onboarding-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.HasPlan);
        Assert.Empty(payload.Tasks);
    }

    [Fact]
    public async Task Get_MyOnboardingStatus_Returns_Ok_With_Own_Plan_And_Task_Progress_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(SelfUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
            var plan = OnboardingPlan.Create(Guid.NewGuid(), companyId, SelfUser, new DateOnly(2026, 7, 1), null, Now);
            plan.Start(Now);
            db.OnboardingPlans.Add(plan);

            var completed = OnboardingTask.Create(
                Guid.NewGuid(), companyId, plan.Id, "Set up workstation", null,
                OnboardingTemplateTaskAssignTo.Manager, new DateOnly(2026, 7, 2), Now);
            completed.Complete(Now);

            var pending = OnboardingTask.Create(
                Guid.NewGuid(), companyId, plan.Id, "Complete paperwork", null,
                OnboardingTemplateTaskAssignTo.NewHire, new DateOnly(2026, 7, 5), Now);

            db.OnboardingTasks.AddRange(completed, pending);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/me/onboarding-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.HasPlan);
        Assert.Equal("InProgress", payload.PlanStatus);
        Assert.Equal(2, payload.TotalTasks);
        Assert.Equal(1, payload.CompletedTasks);
    }

    [Fact]
    public async Task Get_MyOnboardingStatus_Does_Not_Return_Another_Employees_Plan()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(SelfUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
            db.OnboardingPlans.Add(OnboardingPlan.Create(Guid.NewGuid(), companyId, OtherUser, new DateOnly(2026, 7, 1), null, Now));
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/me/onboarding-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.HasPlan);
    }

    private sealed record StatusPayload(
        bool HasPlan, string? PlanStatus, DateOnly? StartDate, int TotalTasks, int CompletedTasks,
        List<TaskItemPayload> Tasks);

    private sealed record TaskItemPayload(Guid Id, string Title, string Status, DateOnly? DueDate, DateTimeOffset? CompletedAt);
}
