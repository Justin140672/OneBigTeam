using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Features.GetOnboardingOverview;

internal sealed class GetOnboardingOverviewHandler(
    OnboardingDbContext dbContext,
    IOutstandingDocumentRequestReader documentReader,
    IOutstandingAssetAcknowledgementReader assetReader,
    IProbationSummaryReader probationReader,
    ITaskLinkBySourceEntityReader taskLinkReader,
    IEmployeeNameReader employeeNameReader)
{
    public async Task<GetOnboardingOverviewResponse> HandleAsync(
        GetOnboardingOverviewRequest request,
        CancellationToken cancellationToken)
    {
        var planTask = dbContext.OnboardingPlans
            .AsNoTracking()
            .Where(p => p.CompanyId == request.CompanyId && p.EmployeeId == request.EmployeeId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var documentsTask = documentReader.GetOutstandingRequestsAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);
        var assetsTask = assetReader.GetOutstandingAcknowledgementsAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);
        var probationTask = probationReader.GetSummaryAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        await Task.WhenAll(planTask, documentsTask, assetsTask, probationTask);

        var plan = planTask.Result;
        var documentsResult = documentsTask.Result;
        var assetsResult = assetsTask.Result;
        var probationResult = probationTask.Result;

        if (plan is null)
        {
            return new GetOnboardingOverviewResponse(
                request.EmployeeId,
                false,
                null,
                null,
                [],
                documentsResult,
                assetsResult,
                probationResult);
        }

        var tasks = await dbContext.OnboardingTasks
            .AsNoTracking()
            .Where(t => t.OnboardingPlanId == plan.Id)
            .ToListAsync(cancellationToken);

        var links = await taskLinkReader.GetTaskLinksAsync(
            request.CompanyId, tasks.Select(t => t.Id), cancellationToken, TaskActionType.Complete);

        var ownerIds = links.Values
            .Select(l => l.AssignedEmployeeId ?? l.AssignedUserId)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var ownerNames = ownerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await employeeNameReader.GetNamesAsync(request.CompanyId, ownerIds, cancellationToken);

        var taskItems = tasks
            .Select(t =>
            {
                links.TryGetValue(t.Id, out var link);
                var ownerId = link?.AssignedEmployeeId ?? link?.AssignedUserId;
                var ownerName = ownerId is { } id && ownerNames.TryGetValue(id, out var name) ? name : null;
                return new OnboardingTaskOverviewItem(
                    t.Id, t.Title, t.Status.ToString(), t.DueDate, t.CreatedAt, t.CompletedAt, t.UpdatedAt,
                    link?.TaskId, ownerId,
                    ownerName ?? (ownerId is not null ? "Unknown" : t.AssignTo == OnboardingTemplateTaskAssignTo.Hr ? "HR" : "Unassigned"));
            })
            .ToList();

        return new GetOnboardingOverviewResponse(
            request.EmployeeId,
            true,
            plan.Status.ToString(),
            plan.StartDate,
            taskItems,
            documentsResult,
            assetsResult,
            probationResult);
    }
}
