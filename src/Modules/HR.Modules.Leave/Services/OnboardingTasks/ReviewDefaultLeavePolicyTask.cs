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
    public int Order => 3;

    // Links straight to the default policy's own edit page
    // ("/companies/{CompanyId:guid}/leave-policies/{Id:guid}") rather than the leave policies
    // search/list screen — "review your default leave policy" means look at that one specific
    // policy, not go find it yourself. Falls back to the plain list route (still company-scoped,
    // "{companyId}" substituted by HR.Web) in the unexpected case no default policy exists yet.
    public async Task<string> GetLinkUrlAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var defaultPolicyId = await dbContext.LeavePolicies
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.IsDefault)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return defaultPolicyId is null
            ? "/companies/{companyId}/leave-policies"
            : $"/companies/{{companyId}}/leave-policies/{defaultPolicyId}";
    }

    public Task<bool> IsCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return dbContext.LeavePolicies
            .AsNoTracking()
            .AnyAsync(p => p.CompanyId == companyId && p.IsDefault && p.UpdatedAt > p.CreatedAt, cancellationToken);
    }
}
