using HR.Modules.Support.Domain;
using HR.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Support.Features.GetSupportDashboard;

// Staff-only, deliberately global across companies — this is a One Big Team internal support
// operations view, not a tenant-scoped feature (see route: no {companyId} segment).
internal sealed class GetSupportDashboardHandler(SupportDbContext db)
{
    private const int TopN = 10;

    public async Task<GetSupportDashboardResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var openCount = await db.SupportRequests
            .AsNoTracking()
            .CountAsync(r => r.Status != SupportRequestStatus.Resolved && r.Status != SupportRequestStatus.Closed, cancellationToken);

        var topFeaturesRaw = await db.SupportRequests
            .AsNoTracking()
            .Where(r => r.Type == SupportRequestType.RequestFeature)
            .GroupBy(r => r.Title)
            .Select(g => new { Title = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(TopN)
            .ToListAsync(cancellationToken);
        var topFeatures = topFeaturesRaw
            .Select(x => new GetSupportDashboardTitleCountDto(x.Title, x.Count))
            .ToList();

        var topProblemsRaw = await db.SupportRequests
            .AsNoTracking()
            .Where(r => r.Type == SupportRequestType.ReportProblem)
            .GroupBy(r => r.Title)
            .Select(g => new { Title = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(TopN)
            .ToListAsync(cancellationToken);
        var topProblems = topProblemsRaw
            .Select(x => new GetSupportDashboardTitleCountDto(x.Title, x.Count))
            .ToList();

        var typeBreakdown = await db.SupportRequests
            .AsNoTracking()
            .GroupBy(r => r.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        double? averageHours = null;
        {
            var samples = await db.SupportRequests
                .AsNoTracking()
                .Select(r => new
                {
                    r.CreatedAt,
                    FirstStaffResponseAt = db.SupportResponses
                        .Where(resp => resp.SupportRequestId == r.Id && resp.IsStaffResponse)
                        .OrderBy(resp => resp.CreatedAt)
                        .Select(resp => (DateTimeOffset?)resp.CreatedAt)
                        .FirstOrDefault()
                })
                .Where(x => x.FirstStaffResponseAt != null)
                .ToListAsync(cancellationToken);

            if (samples.Count > 0)
            {
                averageHours = samples.Average(x => (x.FirstStaffResponseAt!.Value - x.CreatedAt).TotalHours);
            }
        }

        return new GetSupportDashboardResponse(
            openCount,
            averageHours,
            topFeatures,
            topProblems,
            typeBreakdown.Select(x => new GetSupportDashboardTypeBreakdownDto(x.Type.ToString(), x.Count)).ToList());
    }
}
