using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetEmployeeTimeline;

internal sealed class GetEmployeeTimelineHandler(
    EmployeesDbContext dbContext,
    IEmployeeNameReader employeeNameReader)
{
    private static readonly GetEmployeeTimelineResponse Empty = new([], 0, 1, 0, 0);

    public async Task<Result<GetEmployeeTimelineResponse>> HandleAsync(
        GetEmployeeTimelineRequest request,
        Guid callerId,
        bool callerIsHr,
        CancellationToken cancellationToken)
    {
        var target = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId && e.Id == request.EmployeeId)
            .Select(e => new { e.Id, e.ManagerId })
            .FirstOrDefaultAsync(cancellationToken);

        if (target is null)
            return Result.Failure<GetEmployeeTimelineResponse>(
                Error.NotFound($"Employee '{request.EmployeeId}' was not found."));

        var isSelf = callerId == target.Id;
        var isManager = target.ManagerId is not null && target.ManagerId == callerId;

        // Caller has no relationship at all to the target employee (not HR, not self, not their
        // manager). This is a visibility SCOPE gap rather than an authentication/authorization
        // boundary breach — different legitimate callers simply see different subsets of the same
        // timeline — so we return an empty (but successful) result instead of failing the request.
        if (!callerIsHr && !isSelf && !isManager)
            return Result.Success(Empty with { PageNumber = request.PageNumber, PageSize = request.PageSize });

        var query = dbContext.EmployeeTimelineEntries
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId && e.EmployeeId == request.EmployeeId);

        // Push the three-tier visibility check into the query itself rather than materialising
        // rows the caller isn't allowed to see. Equivalent to
        // EmployeeTimelineVisibilityResolver.CanView but expressed so EF can translate it to SQL.
        query = query.Where(e =>
            (callerIsHr) ||
            (e.Visibility == EmployeeTimelineVisibility.EmployeeAndHr && isSelf) ||
            (e.Visibility == EmployeeTimelineVisibility.AuthorisedInternal && (isSelf || isManager)));

        var totalCount = await query.CountAsync(cancellationToken);

        var entries = await query
            .OrderByDescending(e => e.EventDate)
            .ThenByDescending(e => e.CreatedDate)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var performedByIds = entries
            .Where(e => e.PerformedByUserId.HasValue)
            .Select(e => e.PerformedByUserId!.Value)
            .Distinct()
            .ToList();

        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, performedByIds, cancellationToken);

        var items = entries
            .Select(e => new EmployeeTimelineItem(
                e.Id,
                e.EventDate,
                e.EventType,
                e.Category,
                e.Title,
                e.Summary,
                ResolvePerformedBy(e.PerformedByUserId, names),
                e.SourceModule,
                e.SourceRecordId))
            .ToList();

        var totalPages = request.PageSize == 0 ? 0 : (int)Math.Ceiling((double)totalCount / request.PageSize);

        return Result.Success(new GetEmployeeTimelineResponse(
            items, totalCount, request.PageNumber, request.PageSize, totalPages));
    }

    private static string ResolvePerformedBy(Guid? performedByUserId, IReadOnlyDictionary<Guid, string> names)
    {
        if (!performedByUserId.HasValue)
            return "System";

        return names.TryGetValue(performedByUserId.Value, out var name) ? name : "Unknown";
    }
}
