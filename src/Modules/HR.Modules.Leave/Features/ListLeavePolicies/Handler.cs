using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.ListLeavePolicies;

internal sealed class ListLeavePoliciesHandler(LeaveDbContext dbContext)
{
    public async Task<ListLeavePoliciesResponse> HandleAsync(
        ListLeavePoliciesRequest request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.LeavePolicies
            .AsNoTracking()
            .Where(p => p.CompanyId == request.CompanyId);

        if (request.ActiveOnly == true)
            query = query.Where(p => p.IsActive);

        var items = await query
            .OrderBy(p => p.Name)
            .Select(p => new LeavePolicyItem(
                p.Id,
                p.CompanyId,
                p.Name,
                p.Description,
                p.CarryOverDays,
                p.AllowNegativeBalance,
                p.IsActive,
                p.IsDefault,
                p.CreatedAt))
            .ToListAsync(cancellationToken);

        return new ListLeavePoliciesResponse(items);
    }
}
