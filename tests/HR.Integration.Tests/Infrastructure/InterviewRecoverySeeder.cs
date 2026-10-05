using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

internal static class InterviewRecoverySeeder
{
    public sealed record Interview(Guid CompanyId, Guid VacancyId, Guid ApplicationId, Guid InterviewId);

    public sealed record OutcomeTasks(Guid ReviewTaskId, Guid CompleteTaskId, Guid EmployeeId);

    public sealed record Blocked(
        Guid CompanyId, Guid ApplicationId, Guid InterviewId, Guid TaskId, Guid TasksOperationId, Guid ReconciliationId);

    public static async Task<Interview> SeedInterviewAsync(ApiWebApplicationFactory factory)
    {
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(factory, companyId, now);
        var interviewId = await RecruitmentTestSeeder.SeedInterviewAsync(factory, companyId, seeded.ApplicationId, now);
        return new Interview(companyId, seeded.VacancyId, seeded.ApplicationId, interviewId);
    }

    public static async Task<OutcomeTasks> CreateOutcomeTasksAsync(ApiWebApplicationFactory factory, Interview interview)
    {
        using var scope = factory.Services.CreateScope();
        var creator = scope.ServiceProvider.GetRequiredService<ITaskCreator>();
        var employee = Guid.NewGuid();

        async Task<Guid> Create(TaskActionType type) => await creator.CreateAsync(
            interview.CompanyId, Guid.NewGuid(), type.ToString(), null, TaskPriority.Medium, TaskSource.Recruitment, type,
            null, employee, employee, interview.InterviewId, CancellationToken.None, notifyAssignee: false);

        return new OutcomeTasks(await Create(TaskActionType.Review), await Create(TaskActionType.Complete), employee);
    }

    /// <summary>A completed task with a terminally failed programmatic completion and a Recruitment reconciliation blocked on it.</summary>
    public static async Task<Blocked> SeedBlockedAsync(ApiWebApplicationFactory factory)
    {
        var interview = await SeedInterviewAsync(factory);
        var now = DateTimeOffset.UtcNow;
        var taskId = await TaskSeeder.SeedAsync(
            factory, interview.CompanyId, "Record feedback", source: TaskSource.Recruitment,
            actionType: TaskActionType.Complete, sourceEntityId: interview.InterviewId, status: TaskItemStatus.Completed);

        using var scope = factory.Services.CreateScope();
        var tasksDb = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var state = ProgrammaticTaskCompletion.Create(
            taskId, interview.CompanyId, Guid.NewGuid(), "Open", now, businessEffectAlreadyApplied: true);
        state.Claim(Guid.NewGuid(), now);
        state.RecordFailure("Seeded permanent failure", terminal: true, now);
        tasksDb.ProgrammaticTaskCompletions.Add(state);
        await tasksDb.SaveChangesAsync();

        var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var record = InterviewOutcomeTaskReconciliation.Create(
            Guid.NewGuid(), interview.CompanyId, interview.ApplicationId, interview.InterviewId, Guid.NewGuid(), now);
        record.Block(
            InterviewOutcomeTaskReconciliation.BlockedTaskTerminalFailure, "Seeded permanent failure",
            taskId, state.OperationId, now);
        recruitmentDb.InterviewOutcomeTaskReconciliations.Add(record);
        await recruitmentDb.SaveChangesAsync();

        return new Blocked(
            interview.CompanyId, interview.ApplicationId, interview.InterviewId, taskId, state.OperationId, record.Id);
    }
}
