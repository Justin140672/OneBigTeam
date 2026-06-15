using HR.SharedKernel;
using HR.SharedKernel.Contracts;

namespace HR.Modules.Tasks.Features.CompleteTask.Actions;

/// <summary>
/// When a Probation task is completed, automatically creates a follow-up task
/// to issue the probation outcome letter. The new task is unassigned so HR can
/// pick it up, and is due within three working days.
/// </summary>
internal sealed class ProbationTaskCompletionAction(ITaskCreator taskCreator, IClock clock) : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Probation;

    public async Task ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        var employeeName = ExtractEmployeeName(context.Title);
        var dueDate = DateOnly.FromDateTime(clock.UtcNowOffset().AddDays(3).DateTime);

        await taskCreator.CreateAsync(
            context.CompanyId,
            context.CompletedBy,
            $"Issue probation outcome letter — {employeeName}",
            "Review the outcome of the probation review meeting and issue the appropriate confirmation " +
            "or extension letter to the employee within three working days.",
            TaskPriority.High,
            TaskSource.Probation,
            dueDate,
            assignedEmployeeId: null,
            assignedUserId: null,
            cancellationToken);
    }

    private static string ExtractEmployeeName(string title) =>
        title.Contains(" — ")
            ? title[(title.IndexOf(" — ", StringComparison.Ordinal) + 3)..]
            : title;
}
