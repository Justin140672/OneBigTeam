using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Persistence;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HR.Integration.Tests.Infrastructure.InternalAppointmentTestSeeder;

namespace HR.Integration.Tests.Infrastructure;

internal static class InternalOfferTestHelpers
{
    public const decimal DefaultSalary = 75000m;
    public const string DefaultCurrency = "GBP";

    public static string OfferUrl(Scenario s) =>
        $"/api/companies/{s.CompanyId}/vacancies/{s.VacancyId}/applications/{s.ApplicationId}/offer";

    public static string RecruiterOfferResponseUrl(Scenario s) => OfferUrl(s) + "/response";

    public static string ApplicationUrl(Scenario s) =>
        $"/api/companies/{s.CompanyId}/vacancies/{s.VacancyId}/applications/{s.ApplicationId}";

    public static string InternalOfferUrl(Guid companyId, Guid applicationId) =>
        $"/api/companies/{companyId}/internal-offers/{applicationId}";

    public static string InternalOfferUrl(Scenario s) => InternalOfferUrl(s.CompanyId, s.ApplicationId);

    public static string InternalOfferResponseUrl(Scenario s) => InternalOfferUrl(s) + "/response";

    public static async Task<HttpClient> ClientForUserAsync(
        ApiWebApplicationFactory factory, Guid companyId, Guid userId, params Guid[] roles)
    {
        foreach (var role in roles)
            await TestRoleSeeder.AssignRoleAsync(factory, userId, role, companyId);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    public static Task<HttpClient> EmployeeClientAsync(ApiWebApplicationFactory factory, Scenario s) =>
        ClientForUserAsync(factory, s.CompanyId, s.EmployeeId, HR.Modules.Identity.Domain.SystemRoles.Employee);

    public static async Task<(HttpClient Client, Guid UserId)> RecruiterAsync(ApiWebApplicationFactory factory, Guid companyId)
    {
        var userId = Guid.NewGuid();
        var client = await ClientForUserAsync(
            factory, companyId, userId,
            HR.Modules.Identity.Domain.SystemRoles.Recruiter,
            HR.Modules.Identity.Domain.SystemRoles.HrAdministrator,
            HR.Modules.Identity.Domain.SystemRoles.Employee);
        return (client, userId);
    }

    public static Dictionary<string, object?> OfferBody(Scenario s, Action<Dictionary<string, object?>>? customize = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["proposedStartDate"] = Today.AddDays(14).ToString("yyyy-MM-dd"),
            ["offeredSalary"] = DefaultSalary,
            ["offeredSalaryFrequency"] = "Annual",
            ["currency"] = DefaultCurrency,
            ["proposedManagerId"] = s.World.NewManagerId,
            ["noManager"] = false,
            ["hoursPerWeek"] = 37.5m,
            ["fte"] = 1m,
            ["responseDeadline"] = Today.AddDays(7).ToString("yyyy-MM-dd"),
            ["offerNotes"] = "Welcome to the new team.",
        };
        customize?.Invoke(body);
        return body;
    }

    public static Task<HttpResponseMessage> MakeOfferAsync(
        HttpClient recruiter, Scenario s, Action<Dictionary<string, object?>>? customize = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, OfferUrl(s))
        {
            Content = JsonContent.Create(OfferBody(s, customize)),
        };

        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);

        return recruiter.SendAsync(request);
    }

    public static async Task<JsonElement> MakeOfferOkAsync(
        HttpClient recruiter, Scenario s, Action<Dictionary<string, object?>>? customize = null, string? idempotencyKey = null)
    {
        var response = await MakeOfferAsync(recruiter, s, customize, idempotencyKey);
        await AssertStatusAsync(HttpStatusCode.OK, response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static Task<HttpResponseMessage> RespondAsync(
        HttpClient employee, Scenario s, string decision, int offerVersion = 1, string? reason = null) =>
        employee.PostAsJsonAsync(InternalOfferResponseUrl(s), new { decision, offerVersion, reason });

    public static async Task<JsonElement> RespondOkAsync(
        HttpClient employee, Scenario s, string decision, int offerVersion = 1, string? reason = null)
    {
        var response = await RespondAsync(employee, s, decision, offerVersion, reason);
        await AssertStatusAsync(HttpStatusCode.OK, response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static Task<HttpResponseMessage> RecruiterRespondAsync(
        HttpClient recruiter, Scenario s, string status, string? reason = null) =>
        recruiter.PostAsJsonAsync(RecruiterOfferResponseUrl(s), new { status, reason });

    public static async Task<JsonElement> GetOfferOkAsync(HttpClient client, Scenario s)
    {
        var response = await client.GetAsync(InternalOfferUrl(s));
        await AssertStatusAsync(HttpStatusCode.OK, response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        if (response.StatusCode != expected)
            Assert.Fail($"Expected {(int)expected} {expected} but was {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    public static async Task<List<TaskItem>> GetOfferTasksAsync(ApiWebApplicationFactory factory, Scenario s)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        return await db.TaskItems.AsNoTracking()
            .Where(t => t.CompanyId == s.CompanyId
                        && t.SourceEntityId == s.ApplicationId
                        && t.Source == TaskSource.Recruitment)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();
    }

    public static async Task<List<Notification>> GetNotificationsAsync(
        ApiWebApplicationFactory factory, Guid companyId, Guid recipientId, NotificationType type)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return await db.Notifications.AsNoTracking()
            .Where(n => n.CompanyId == companyId && n.EmployeeId == recipientId && n.Type == type)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync();
    }

    public static async Task<List<Notification>> GetTaskAssignedNotificationsAsync(
        ApiWebApplicationFactory factory, Scenario s, Guid taskId)
    {
        var all = await GetNotificationsAsync(factory, s.CompanyId, s.EmployeeId, NotificationType.TaskAssigned);
        return all.Where(n => n.SourceEntityId == taskId).ToList();
    }

    public static async Task<List<AuditEvent>> GetAuditEventsAsync(
        ApiWebApplicationFactory factory, Guid companyId, Guid entityId, string eventType)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await db.AuditEvents.AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.EntityId == entityId && e.EventType == eventType)
            .ToListAsync();
    }

    public static Dictionary<string, JsonElement> MetadataOf(AuditEvent audit)
    {
        using var doc = JsonDocument.Parse(audit.MetadataJson!);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<List<HR.Modules.Employees.Domain.Compensation>> GetCompensationsAsync(
        ApiWebApplicationFactory factory, Guid employeeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        return await db.Compensations.AsNoTracking().Where(c => c.EmployeeId == employeeId).ToListAsync();
    }

    public static async Task SetTargetProfileAsync(
        ApiWebApplicationFactory factory,
        Scenario s,
        decimal? salaryMin = null,
        HR.Modules.Employees.Contracts.WorkingDays? workingDays = null,
        decimal? hoursPerDay = null,
        int? probationMonths = null,
        string? title = null,
        bool moveToCurrentDepartmentAndLocation = false,
        string? renameTargetDepartmentTo = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var now = DateTimeOffset.UtcNow;

        var profile = await db.PositionProfiles.SingleAsync(p => p.Id == s.World.Target.PositionProfileId);
        profile.Update(
            moveToCurrentDepartmentAndLocation ? s.World.Current.DepartmentId : profile.DepartmentId,
            moveToCurrentDepartmentAndLocation ? s.World.Current.LocationId : profile.LocationId,
            title ?? profile.Title,
            probationMonths,
            workingDays,
            hoursPerDay,
            salaryMin,
            salaryMin is null ? null : salaryMin * 2,
            salaryMin is null ? null : HR.Modules.Employees.Domain.SalaryType.Annual,
            profile.DefaultLeavePolicyId,
            now);

        if (renameTargetDepartmentTo is not null)
        {
            var department = await db.Departments.SingleAsync(d => d.Id == s.World.Target.DepartmentId);
            department.Update(renameTargetDepartmentTo, department.Description, department.ParentDepartmentId, department.ManagerEmployeeId, now);
        }

        await db.SaveChangesAsync();
    }

    public static async Task<Scenario> SeedOfferedAsync(
        ApiWebApplicationFactory factory, Guid companyId, HttpClient recruiter, Action<Dictionary<string, object?>>? customize = null)
    {
        var s = await SeedUnofferedAsync(factory, companyId);
        await MakeOfferOkAsync(recruiter, s, customize);
        return s;
    }
}
