using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Services.OnboardingTasks;

internal sealed class ReviewDefaultLeavePolicyTask(LeaveDbContext dbContext) : IOnboardingTaskDefinition
{
    public string Key => "review-default-leave-policy";
    public string Name => "Review your default leave policy";
    public string Description => "Check the carry-over rules and settings for your default leave policy.";
    public bool IsMandatory => true;
    // HR.Web's leave policies route is company-scoped ("/companies/{CompanyId:guid}/leave-policies")
    // — the "{companyId}" placeholder is substituted by HR.Web with the current company id.
    public string LinkUrl => "/companies/{companyId}/leave-policies";
    public int Order => 3;

    public Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return dbContext.LeavePolicies
            .AsNoTracking()
            .AnyAsync(p => p.CompanyId == companyId && p.IsDefault && p.UpdatedAt > p.CreatedAt, cancellationToken);
    }
}
