using HR.SharedKernel;
using HR.Modules.Marketing.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.SetMarketingRoadmapItemPublication;

internal sealed class SetMarketingRoadmapItemPublicationHandler(
    MarketingDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<SetMarketingRoadmapItemPublicationResponse>> HandleAsync(
        SetMarketingRoadmapItemPublicationRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var item = await dbContext.MarketingRoadmapItems
            .SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (item is null)
        {
            return Result.Failure<SetMarketingRoadmapItemPublicationResponse>(
                Error.NotFound("Marketing roadmap item not found."));
        }

        var previouslyPublished = item.IsPublished;

        if (request.IsPublished)
        {
            item.Publish(currentUser.UserId, now);
        }
        else
        {
            item.Unpublish(currentUser.UserId, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new MarketingRoadmapItemPublicationChangedAuditEvent(
                item.Id,
                currentUser.UserId,
                now,
                previouslyPublished,
                item.IsPublished),
            cancellationToken);

        return Result.Success(new SetMarketingRoadmapItemPublicationResponse(
            item.Id,
            item.IsPublished,
            item.UpdatedAt,
            item.UpdatedByUserId));
    }
}
