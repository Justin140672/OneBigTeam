using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ListExternalRecruiters;

internal sealed class ListExternalRecruitersHandler(RecruitmentDbContext db)
{
    public async Task<Result<ListExternalRecruitersResponse>> HandleAsync(
        ListExternalRecruitersRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.ExternalRecruiters
            .AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(r =>
                r.AgencyName.ToLower().Contains(search) ||
                (r.ContactName != null && r.ContactName.ToLower().Contains(search)));
        }

        if (request.IsActive.HasValue)
            query = query.Where(r => r.IsActive == request.IsActive.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(r => r.AgencyName)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(r => new ExternalRecruiterListItem(
                r.Id,
                r.AgencyName,
                r.ContactName,
                r.ContactEmail,
                r.ContactTelephone,
                r.IsActive,
                // Ticket #81: VacancyRecruiterAssignment (with its own active/historical rows) has been
                // removed — this now simply counts Vacancy rows where AssignedRecruiterId currently
                // points at this recruiter. Unlike the old all-time count, this is a current-snapshot
                // figure only (a vacancy that had this recruiter assigned and was later reassigned no
                // longer counts) — see GetExternalRecruiterActivitySummaryHandler's remarks for the
                // same underlying trade-off.
                db.Vacancies.Count(v => v.AssignedRecruiterId == r.Id),
                r.CreatedAt))
            .ToListAsync(cancellationToken);

        var totalPages = request.PageSize == 0 ? 0 : (int)Math.Ceiling((double)totalCount / request.PageSize);

        return Result.Success(new ListExternalRecruitersResponse(items, totalCount, request.PageNumber, request.PageSize, totalPages));
    }
}
