using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class RecordInterviewOutcomeTaskReconciliationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0000c7-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public RecordInterviewOutcomeTaskReconciliationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(() => TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter)).GetAwaiter().GetResult();
    }

    private async Task<(Guid Review, Guid Complete)> CreateTasksAsync(Guid companyId, Guid interviewId)
    {
        using var scope = _factory.Services.CreateScope();
        var creator = scope.ServiceProvider.GetRequiredService<ITaskCreator>();
        var employee = Guid.NewGuid();

        async Task<Guid> Create(TaskActionType type) => await creator.CreateAsync(
            companyId, Guid.NewGuid(), type.ToString(), null, TaskPriority.Medium, TaskSource.Recruitment, type,
            null, employee, employee, interviewId, CancellationToken.None, notifyAssignee: false);

        return (await Create(TaskActionType.Review), await Create(TaskActionType.Complete));
    }

    private async Task AssertFinalStateAsync(Guid companyId, Guid interviewId, (Guid Review, Guid Complete) tasks)
    {
        using var scope = _factory.Services.CreateScope();
        var tasksDb = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        Assert.Equal(TaskItemStatus.Cancelled, (await tasksDb.TaskItems.SingleAsync(t => t.Id == tasks.Review)).Status);
        Assert.Equal(TaskItemStatus.Completed, (await tasksDb.TaskItems.SingleAsync(t => t.Id == tasks.Complete)).Status);
        Assert.NotNull((await tasksDb.ProgrammaticTaskCompletions.SingleAsync(c => c.TaskId == tasks.Complete)).ConfirmedAt);

        var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var record = await recruitmentDb.InterviewOutcomeTaskReconciliations
            .SingleAsync(r => r.CompanyId == companyId && r.InterviewId == interviewId);
        Assert.NotNull(record.CompletedAt);
        Assert.NotNull(record.AuditDeliveredAt);
    }

    [Fact]
    public async Task Direct_Outcome_Recording_Reconciles_Feedback_And_Review_Tasks_Through_The_Tasks_Contract()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        var tasks = await CreateTasksAsync(companyId, interviewId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/interviews/{interviewId}/outcome",
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId, interviewId, outcome = "Passed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertFinalStateAsync(companyId, interviewId, tasks);
    }

    [Fact]
    public async Task Task_Driven_Recording_Reaches_The_Same_Final_State_Via_The_Reconciliation_Job()
    {
        var companyId = Guid.NewGuid();
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, Now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(_factory, companyId, seeded.ApplicationId, Now);
        var tasks = await CreateTasksAsync(companyId, interviewId);

        using (var scope = _factory.Services.CreateScope())
        {
            var recorder = scope.ServiceProvider.GetRequiredService<InterviewOutcomeRecorder>();
            var result = await recorder.RecordAsync(
                new RecordInterviewOutcomeRequest
                {
                    CompanyId = companyId,
                    VacancyId = seeded.VacancyId,
                    ApplicationId = seeded.ApplicationId,
                    InterviewId = interviewId,
                    Outcome = InterviewOutcome.Failed,
                },
                Guid.NewGuid(), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<InterviewTaskCleanupReconciliationJob>();
            await job.ExecuteAsync();
            await job.ExecuteAsync();
        }

        await AssertFinalStateAsync(companyId, interviewId, tasks);
    }
}
