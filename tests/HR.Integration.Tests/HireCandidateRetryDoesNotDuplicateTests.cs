using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// NFR-08 failure-injection: HireCandidateHandler provisions the employee in the Employees
/// DbContext (its own transaction) BEFORE it commits application.RecordHire + candidate.LinkToEmployee
/// to the Recruitment schema. If the process dies between those two commits, the candidate is left
/// unlinked. This test reproduces that partial-failure state (by reverting the Recruitment-side
/// changes after a successful hire) and then retries the hire, asserting that the retry does NOT
/// create a second employee / probation record / onboarding plan — the stable
/// SourceReference "recruitment:application:{applicationId}" makes provisioning idempotent.
/// </summary>
[Collection("Integration")]
public class HireCandidateRetryDoesNotDuplicateTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00001a-0000-0000-0000-000000000042");

    public HireCandidateRetryDoesNotDuplicateTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Retrying_Hire_After_Lost_Recruitment_Commit_Does_Not_Duplicate_Employee_Or_Downstream_Records()
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, RecruiterUser, SystemRoles.Recruiter, companyId);

        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var vacancy = await (await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = referenceData.PositionProfileId,
            advertTitle = "Engineer",
            hiringManagerId = Guid.NewGuid(),
        })).Content.ReadFromJsonAsync<Payload>();

        var candidate = await (await client.PostAsJsonAsync($"/api/companies/{companyId}/candidates", new
        {
            companyId,
            firstName = "Robin",
            lastName = "Fisher",
            email = $"robin.fisher.{Guid.NewGuid():N}@example.com",
        })).Content.ReadFromJsonAsync<Payload>();

        var application = await (await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancy!.Id}/applications", new
            {
                companyId,
                vacancyId = vacancy.Id,
                candidateId = candidate!.Id,
            })).Content.ReadFromJsonAsync<Payload>();

        object HireBody() => new
        {
            companyId,
            vacancyId = vacancy.Id,
            applicationId = application!.Id,
            startDate = new DateOnly(2026, 9, 1).ToString("yyyy-MM-dd"),
            dateOfBirth = new DateOnly(1990, 1, 1).ToString("yyyy-MM-dd"),
            nationality = "British",
            gender = "Prefer not to say",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            employmentTypeId = referenceData.EmploymentTypeId,
        };

        // 1. First hire succeeds.
        var firstHire = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancy.Id}/applications/{application!.Id}/hire", HireBody());
        Assert.Equal(HttpStatusCode.OK, firstHire.StatusCode);
        var hire = await firstHire.Content.ReadFromJsonAsync<HirePayload>();
        var employeeId = hire!.EmployeeId;

        // Baseline counts after the (fully successful) first hire.
        var (employeesBefore, probationBefore, onboardingBefore) = await CountsAsync(companyId, employeeId);

        // 2. Reproduce the partial failure: the Recruitment-side commit was lost — unlink the
        //    candidate and move the application back to a non-terminal stage, as if RecordHire /
        //    LinkToEmployee never happened.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var nonTerminalStageId = (await db.Database
                .SqlQueryRaw<Guid>(
                    "SELECT id AS \"Value\" FROM recruitment.recruitment_stages " +
                    "WHERE company_id = {0} AND is_terminal = false AND is_active = true " +
                    "ORDER BY display_order LIMIT 1", companyId)
                .ToListAsync()).First();

            await db.Database.ExecuteSqlRawAsync(
                "UPDATE recruitment.candidates SET employee_id = NULL WHERE id = {0}", candidate.Id);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE recruitment.applications SET current_stage_id = {0} WHERE id = {1}",
                nonTerminalStageId, application.Id);
        }

        // 3. Retry the hire.
        var retryHire = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancy.Id}/applications/{application.Id}/hire", HireBody());
        Assert.Equal(HttpStatusCode.OK, retryHire.StatusCode);
        var retried = await retryHire.Content.ReadFromJsonAsync<HirePayload>();

        // Same employee — provisioning was idempotent on the application source reference.
        Assert.Equal(employeeId, retried!.EmployeeId);

        var (employeesAfter, probationAfter, onboardingAfter) = await CountsAsync(companyId, employeeId);
        Assert.Equal(employeesBefore, employeesAfter);
        Assert.Equal(probationBefore, probationAfter);
        Assert.Equal(onboardingBefore, onboardingAfter);
        Assert.Equal(1, employeesAfter);

        // Candidate ends up linked to that one employee.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var linkedEmployeeId = (await db.Database
                .SqlQueryRaw<Guid>("SELECT employee_id AS \"Value\" FROM recruitment.candidates WHERE id = {0}", candidate.Id)
                .ToListAsync()).First();
            Assert.Equal(employeeId, linkedEmployeeId);
        }
    }

    private async Task<(int Employees, int Probation, int Onboarding)> CountsAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();

        var employees = await db.Employees.CountAsync(e => e.Id == employeeId);
        var probation = (await db.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM probation.probation_records WHERE employee_id = {0}", employeeId)
            .ToListAsync()).First();
        var onboarding = (await db.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM onboarding.onboarding_plans WHERE employee_id = {0}", employeeId)
            .ToListAsync()).First();

        return (employees, probation, onboarding);
    }

    private sealed record Payload(Guid Id);
    private sealed record HirePayload(Guid Id, Guid EmployeeId);
}
