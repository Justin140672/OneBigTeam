using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Features.GetOnboardingOverview;

internal sealed class GetOnboardingOverviewHandler(
    OnboardingDbContext dbContext,
    IOutstandingDocumentRequestReader documentReader,
    IOutstandingAssetAcknowledgementReader assetReader,
    IProbationSummaryReader probationReader)
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

        var taskItems = tasks
            .Select(t => new OnboardingTaskOverviewItem(t.Id, t.Title, t.Status.ToString(), t.DueDate))
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
