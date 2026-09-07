using HR.SharedKernel;
using HR.Modules.Marketing.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.ReorderMarketingRoadmapItems;

internal sealed class ReorderMarketingRoadmapItemsHandler(
    MarketingDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<ReorderMarketingRoadmapItemsResponse>> HandleAsync(
        ReorderMarketingRoadmapItemsRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var orderedIds = request.OrderedIds;

        var items = await dbContext.MarketingRoadmapItems
            .Where(r => orderedIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        if (items.Count != orderedIds.Count)
        {
            return Result.Failure<ReorderMarketingRoadmapItemsResponse>(
                Error.NotFound("One or more marketing roadmap items could not be found."));
        }

        var previousOrder = items
            .OrderBy(r => r.DisplayOrder)
            .Select(r => r.Id)
            .ToList();

        var byId = items.ToDictionary(r => r.Id);
        for (var index = 0; index < orderedIds.Count; index++)
        {
            byId[orderedIds[index]].SetDisplayOrder(index, currentUser.UserId, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new MarketingRoadmapItemsReorderedAuditEvent(
                currentUser.UserId,
                now,
                previousOrder,
                orderedIds.ToList()),
            cancellationToken);

        return Result.Success(new ReorderMarketingRoadmapItemsResponse(
            orderedIds.Select((id, index) => new ReorderedMarketingRoadmapItemDto(id, index)).ToList()));
    }
}
