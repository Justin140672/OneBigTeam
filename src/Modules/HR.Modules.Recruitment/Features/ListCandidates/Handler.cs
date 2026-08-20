using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ListCandidates;

internal sealed class ListCandidatesHandler(RecruitmentDbContext db)
{
    public async Task<Result<ListCandidatesResponse>> HandleAsync(
        ListCandidatesRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.Candidates
            .AsNoTracking()
            .Where(c => c.CompanyId == request.CompanyId);

        // Deactivated candidates are excluded from active searches, pipelines, selectors and default
        // lists by default (see the ticket's "Include inactive candidates" filter requirement) — this
        // handler backs the plain candidate list, the application-creation candidate selector and any
        // other consumer of ListCandidates, so this default applies everywhere unless the caller
        // explicitly opts in via IncludeInactive.
        if (!request.IncludeInactive)
            query = query.Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(c =>
                c.FirstName.ToLower().Contains(search) ||
                c.LastName.ToLower().Contains(search) ||
                (c.FirstName.ToLower() + " " + c.LastName.ToLower()).Contains(search) ||
                c.Email.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => new CandidateListItem(
                c.Id,
                c.FirstName,
                c.LastName,
                c.Email,
                c.Phone,
                c.IsActive,
                c.CreatedAt))
            .ToListAsync(cancellationToken);

        var totalPages = request.PageSize == 0 ? 0 : (int)Math.Ceiling((double)totalCount / request.PageSize);

        return Result.Success(new ListCandidatesResponse(items, totalCount, request.PageNumber, request.PageSize, totalPages));
    }
}
