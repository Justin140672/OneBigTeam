using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.ListLeaveTypes;

internal sealed class ListLeaveTypesHandler(LeaveDbContext db)
{
    public async Task<Result<ListLeaveTypesResponse>> HandleAsync(
        ListLeaveTypesRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.LeaveTypes
            .AsNoTracking()
            .Where(t => t.CompanyId == request.CompanyId);

        if (request.IsActive is not null)
            query = query.Where(t => t.IsActive == request.IsActive);

        var items = await query
            .OrderBy(t => t.Name)
            .Select(t => new LeaveTypeItem(
                t.Id, t.CompanyId, t.Name, t.Code,
                t.DefaultEntitlementDays,
                t.AccrualMethod.ToString(),
                t.Behaviour.ToString(),
                t.IsActive, t.HasBalance, t.CreatedAt, t.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListLeaveTypesResponse(items));
    }
}
