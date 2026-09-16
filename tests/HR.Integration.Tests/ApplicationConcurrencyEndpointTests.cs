using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 6 (P1) optimistic concurrency rollout: PostgreSQL, real-connection coverage for
/// Application.Version, exercised through the real HTTP endpoints (see ApplicationConcurrencyHandlerTests
/// in HR.Modules.Recruitment.Tests for the in-memory unit-level equivalent). The scenario is two
/// genuinely concurrent requests both acting on the SAME Application at once — one HireCandidate, one
/// RejectCandidate — fired via Task.WhenAll against two independent HttpClient instances, mirroring
/// AutoApprovingLeaveSubmissionConcurrencyEndpointTests's pattern for forcing a real overlapping DB
/// write race. Only one of Hire/Reject can win: the loser's SaveChangesWithConcurrencyAsync call hits
/// a genuinely stale Application.Version row and is translated to a clean 409 ("concurrency"), never a
/// 500, and the Application/Candidate end up in a state consistent with whichever transition actually
/// committed — never a contradictory mix (e.g. Hired stage with no Employee, or an Employee created but
/// the Candidate left unlinked with the application Rejected).
///
/// Ticket 16 (P2): now uses <see cref="ApplicationSaveBarrier"/> so both requests are
/// guaranteed to have loaded the same stale Application.Version before either is allowed to save —
/// without this, an unsynchronised Task.WhenAll can (rarely) let the loser's read happen strictly
/// after the winner's write already committed, which would surface as a business-validation rejection
/// rather than a genuine version conflict. See IdempotentApplicationTransitionConcurrencyEndpointTests
/// for the equivalent idempotent-keyed coverage and the barrier's own doc comment for how it works.
/// </summary>
[Collection("Integration")]
public class ApplicationConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0f00c6-0000-0000-0000-000000000001");

    public ApplicationConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAs(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, RecruiterUser, companyId);
        return client;
    }

    [Fact]
    public async Task Concurrent_Hire_And_Reject_On_Same_Application_Exactly_One_Wins_And_State_Is_Consistent()
    {
        var companyId = Guid.NewGuid();
        var clientA = await ClientAs(companyId);
        var clientB = await ClientAs(companyId);

        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var vacancy = await (await clientA.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = referenceData.PositionProfileId,
            advertTitle = "Engineer",
            hiringManagerId = Guid.NewGuid(),
        })).Content.ReadFromJsonAsync<Payload>();

        var candidate = await (await clientA.PostAsJsonAsync($"/api/companies/{companyId}/candidates", new
        {
            companyId,
            firstName = "Robin",
            lastName = "Ashworth",
            email = $"robin.ashworth.{Guid.NewGuid():N}@example.com",
        })).Content.ReadFromJsonAsync<Payload>();

        var application = await (await clientA.PostAsJsonAsync(
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
            startDate = new DateOnly(2026, 10, 1).ToString("yyyy-MM-dd"),
            dateOfBirth = new DateOnly(1990, 1, 1).ToString("yyyy-MM-dd"),
            nationality = "British",
            gender = "Prefer not to say",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            employmentTypeId = referenceData.EmploymentTypeId,
        };

        object RejectBody() => new { companyId, rejectionReason = "Role filled by another candidate" };

        HttpResponseMessage hireResponse, rejectResponse;
        using (ApplicationSaveBarrier.Arm(application!.Id))
        {
            var hireTask = clientA.PostAsJsonAsync(
                $"/api/companies/{companyId}/vacancies/{vacancy.Id}/applications/{application.Id}/hire", HireBody());
            var rejectTask = clientB.PostAsJsonAsync(
                $"/api/companies/{companyId}/vacancies/{vacancy.Id}/applications/{application.Id}/reject", RejectBody());

            var responses = await Task.WhenAll(hireTask, rejectTask);
            hireResponse = responses[0];
            rejectResponse = responses[1];
        }

        // Every response must be a clean 200 or a clean 409 ("concurrency"/"conflict") — never a 500,
        // and never both succeeding (that would mean the loser's stale write silently landed instead
        // of being rejected).
        Assert.True(
            hireResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"Unexpected hire status: {hireResponse.StatusCode}");
        Assert.True(
            rejectResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"Unexpected reject status: {rejectResponse.StatusCode}");

        var succeeded = new[] { hireResponse, rejectResponse }.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflicted = new[] { hireResponse, rejectResponse }.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, succeeded);
        Assert.Equal(1, conflicted);

        var hireWon = hireResponse.StatusCode == HttpStatusCode.OK;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var savedApplication = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == application.Id);
        var savedCandidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == candidate.Id);
        var hiredStageId = await db.RecruitmentStages.AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.Name == "Hired")
            .Select(s => s.Id)
            .SingleAsync();
        var rejectedStageId = await db.RecruitmentStages.AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.Name == "Rejected")
            .Select(s => s.Id)
            .SingleAsync();

        if (hireWon)
        {
            // Hire committed: the application is on the Hired stage and the candidate IS linked to an
            // employee — regardless of whether the Reject side had already provisioned nothing (it
            // never provisions anything) there is no contradictory state here.
            Assert.Equal(hiredStageId, savedApplication.CurrentStageId);
            Assert.NotNull(savedCandidate.EmployeeId);

            var hirePayload = await hireResponse.Content.ReadFromJsonAsync<HirePayload>();
            Assert.Equal(savedCandidate.EmployeeId, hirePayload!.EmployeeId);
        }
        else
        {
            // Reject committed: the application is on the Rejected stage and the candidate must NOT be
            // linked to an employee. Per the documented NFR-08 limitation, the Employee row may still
            // have been provisioned in the Employees module (via the idempotent SourceReference-keyed
            // call) before Hire's own concurrency-guarded save lost the race — that alone is not a bug
            // as long as the Candidate was never linked to it and the Application ends up Rejected.
            Assert.Equal(rejectedStageId, savedApplication.CurrentStageId);
            Assert.Null(savedCandidate.EmployeeId);
        }

        // History reflects exactly one committed stage change — the winner's — never both.
        var historyCount = await db.ApplicationStageHistoryEntries.CountAsync(h => h.ApplicationId == application.Id);
        Assert.Equal(1, historyCount);
    }

    private sealed record Payload(Guid Id);
    private sealed record HirePayload(Guid Id, Guid EmployeeId);
}
