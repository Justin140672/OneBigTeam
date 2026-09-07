using HR.SharedKernel;
using HR.Modules.Marketing.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.UpdateMarketingFeature;

internal sealed class UpdateMarketingFeatureHandler(
    MarketingDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<UpdateMarketingFeatureResponse>> HandleAsync(
        UpdateMarketingFeatureRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var slug = (request.Slug ?? string.Empty).Trim().ToLowerInvariant();

        var feature = await dbContext.MarketingFeatures
            .SingleOrDefaultAsync(f => f.Id == request.Id, cancellationToken);

        if (feature is null)
        {
            return Result.Failure<UpdateMarketingFeatureResponse>(
                Error.NotFound("Marketing feature not found."));
        }

        var slugTaken = await dbContext.MarketingFeatures
            .AnyAsync(f => f.Slug == slug && f.Id != request.Id, cancellationToken);

        if (slugTaken)
        {
            return Result.Failure<UpdateMarketingFeatureResponse>(
                Error.Conflict($"A marketing feature with slug '{slug}' already exists."));
        }

        var previous = new MarketingFeatureAuditSnapshot(
            feature.Slug, feature.Title, feature.Summary, feature.IsPublished, feature.DisplayOrder,
            feature.DeliveryStatus.ToString());

        var update = feature.Update(
            slug,
            request.IconName,
            request.Title,
            request.Summary,
            request.Intro,
            request.DetailedContent,
            request.Benefits ?? [],
            request.YouTubeId,
            request.DisplayOrder,
            request.DeliveryStatus,
            currentUser.UserId,
            now);

        if (update.IsFailure)
        {
            return Result.Failure<UpdateMarketingFeatureResponse>(update.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new MarketingFeatureUpdatedAuditEvent(
                feature.Id,
                currentUser.UserId,
                now,
                previous,
                new MarketingFeatureAuditSnapshot(
                    feature.Slug, feature.Title, feature.Summary, feature.IsPublished, feature.DisplayOrder,
                    feature.DeliveryStatus.ToString())),
            cancellationToken);

        return Result.Success(new UpdateMarketingFeatureResponse(
            feature.Id,
            feature.Slug,
            feature.Title,
            feature.IsPublished,
            feature.DisplayOrder,
            feature.DeliveryStatus.ToString(),
            feature.UpdatedAt,
            feature.UpdatedByUserId));
    }
}
