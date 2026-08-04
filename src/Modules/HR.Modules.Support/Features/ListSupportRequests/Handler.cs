using HR.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Support.Features.ListSupportRequests;

internal sealed class ListSupportRequestsHandler(SupportDbContext db)
{
    public async Task<List<ListSupportRequestsResponse>> HandleAsync(
        ListSupportRequestsRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.SupportRequests
            .AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId);

        if (request.Status is not null)
            query = query.Where(r => r.Status == request.Status);

        var requests = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        if (requests.Count == 0)
            return [];

        var requestIds = requests.Select(r => r.Id).ToList();

        var latestResponses = await db.SupportResponses
            .AsNoTracking()
            .Where(r => requestIds.Contains(r.SupportRequestId))
            .GroupBy(r => r.SupportRequestId)
            .Select(g => g.OrderByDescending(r => r.CreatedAt).First())
            .ToDictionaryAsync(r => r.SupportRequestId, cancellationToken);

        return requests
            .Select(r =>
            {
                latestResponses.TryGetValue(r.Id, out var latest);
                var snippet = latest is null
                    ? null
                    : (latest.BodyHtml.Length > 160 ? latest.BodyHtml[..160] + "…" : latest.BodyHtml);

                return new ListSupportRequestsResponse(
                    r.Id,
                    r.ReferenceNumber,
                    r.Type.ToString(),
                    r.Title,
                    r.Priority.ToString(),
                    r.Status.ToString(),
                    r.CreatedAt,
                    r.UpdatedAt,
                    snippet);
            })
            .ToList();
    }
}
