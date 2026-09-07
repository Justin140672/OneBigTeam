using HR.SharedKernel;
using HR.Modules.Marketing.Domain;
using HR.Modules.Marketing.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.CreateMarketingRoadmapItem;

internal sealed class CreateMarketingRoadmapItemHandler(
    MarketingDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<CreateMarketingRoadmapItemResponse>> HandleAsync(
        CreateMarketingRoadmapItemRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var product = await dbContext.MarketingProducts
            .SingleOrDefaultAsync(p => p.Id == MarketingProduct.SingletonId, cancellationToken);

        if (product is null)
        {
            product = MarketingProduct.CreateDefault(now);
            dbContext.MarketingProducts.Add(product);
        }

        var created = MarketingRoadmapItem.Create(
            Guid.NewGuid(),
            product.Id,
            request.Title,
            request.Description,
            request.IconName,
            request.DeliveryStatus,
            request.DisplayOrder,
            currentUser.UserId,
            now);

        if (created.IsFailure)
        {
            return Result.Failure<CreateMarketingRoadmapItemResponse>(created.Error);
        }

        var item = created.Value!;
        dbContext.MarketingRoadmapItems.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new MarketingRoadmapItemCreatedAuditEvent(
                item.Id,
                currentUser.UserId,
                now,
                new MarketingRoadmapItemAuditSnapshot(
                    item.Title, item.Description, item.IsPublished, item.DisplayOrder,
                    item.DeliveryStatus.ToString())),
            cancellationToken);

        return Result.Success(new CreateMarketingRoadmapItemResponse(
            item.Id,
            item.Title,
            item.IsPublished,
            item.DisplayOrder,
            item.DeliveryStatus.ToString(),
            item.CreatedAt,
            item.UpdatedAt));
    }
}
