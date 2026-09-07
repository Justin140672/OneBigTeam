using HR.SharedKernel;
using HR.Modules.Marketing.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.SetMarketingFeaturePublication;

internal sealed class SetMarketingFeaturePublicationHandler(
    MarketingDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<SetMarketingFeaturePublicationResponse>> HandleAsync(
        SetMarketingFeaturePublicationRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var feature = await dbContext.MarketingFeatures
            .SingleOrDefaultAsync(f => f.Id == request.Id, cancellationToken);

        if (feature is null)
        {
            return Result.Failure<SetMarketingFeaturePublicationResponse>(
                Error.NotFound("Marketing feature not found."));
        }

        var previouslyPublished = feature.IsPublished;

        if (request.IsPublished)
        {
            feature.Publish(currentUser.UserId, now);
        }
        else
        {
            feature.Unpublish(currentUser.UserId, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new MarketingFeaturePublicationChangedAuditEvent(
                feature.Id,
                currentUser.UserId,
                now,
                previouslyPublished,
                feature.IsPublished),
            cancellationToken);

        return Result.Success(new SetMarketingFeaturePublicationResponse(
            feature.Id,
            feature.IsPublished,
            feature.UpdatedAt,
            feature.UpdatedByUserId));
    }
}
