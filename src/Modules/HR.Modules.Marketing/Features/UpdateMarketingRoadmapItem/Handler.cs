using HR.SharedKernel;
using HR.Modules.Marketing.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.UpdateMarketingRoadmapItem;

internal sealed class UpdateMarketingRoadmapItemHandler(
    MarketingDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<UpdateMarketingRoadmapItemResponse>> HandleAsync(
        UpdateMarketingRoadmapItemRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var item = await dbContext.MarketingRoadmapItems
            .SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (item is null)
        {
            return Result.Failure<UpdateMarketingRoadmapItemResponse>(
                Error.NotFound("Marketing roadmap item not found."));
        }

        var previous = new MarketingRoadmapItemAuditSnapshot(
            item.Title, item.Description, item.IsPublished, item.DisplayOrder, item.DeliveryStatus.ToString());

        var update = item.Update(
            request.Title,
            request.Description,
            request.IconName,
            request.DeliveryStatus,
            request.DisplayOrder,
            currentUser.UserId,
            now);

        if (update.IsFailure)
        {
            return Result.Failure<UpdateMarketingRoadmapItemResponse>(update.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new MarketingRoadmapItemUpdatedAuditEvent(
                item.Id,
                currentUser.UserId,
                now,
                previous,
                new MarketingRoadmapItemAuditSnapshot(
                    item.Title, item.Description, item.IsPublished, item.DisplayOrder, item.DeliveryStatus.ToString())),
            cancellationToken);

        return Result.Success(new UpdateMarketingRoadmapItemResponse(
            item.Id,
            item.Title,
            item.IsPublished,
            item.DisplayOrder,
            item.DeliveryStatus.ToString(),
            item.UpdatedAt,
            item.UpdatedByUserId));
    }
}
