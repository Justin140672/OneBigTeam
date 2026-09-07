using HR.Infrastructure.Abstractions;
using HR.Modules.Marketing.Domain;
using HR.Modules.Marketing.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.CreateMarketingFeature;

internal sealed class CreateMarketingFeatureHandler(
    MarketingDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<CreateMarketingFeatureResponse>> HandleAsync(
        CreateMarketingFeatureRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var slug = (request.Slug ?? string.Empty).Trim().ToLowerInvariant();

        var product = await dbContext.MarketingProducts
            .SingleOrDefaultAsync(p => p.Id == MarketingProduct.SingletonId, cancellationToken);

        if (product is null)
        {
            product = MarketingProduct.CreateDefault(now);
            dbContext.MarketingProducts.Add(product);
        }

        var slugTaken = await dbContext.MarketingFeatures
            .AnyAsync(f => f.Slug == slug, cancellationToken);

        if (slugTaken)
        {
            return Result.Failure<CreateMarketingFeatureResponse>(
                Error.Conflict($"A marketing feature with slug '{slug}' already exists."));
        }

        var created = MarketingFeature.Create(
            Guid.NewGuid(),
            product.Id,
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

        if (created.IsFailure)
        {
            return Result.Failure<CreateMarketingFeatureResponse>(created.Error);
        }

        var feature = created.Value!;
        dbContext.MarketingFeatures.Add(feature);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new MarketingFeatureCreatedAuditEvent(
                feature.Id,
                currentUser.UserId,
                now,
                new MarketingFeatureAuditSnapshot(
                    feature.Slug,
                    feature.Title,
                    feature.Summary,
                    feature.IsPublished,
                    feature.DisplayOrder,
                    feature.DeliveryStatus.ToString())),
            cancellationToken);

        return Result.Success(new CreateMarketingFeatureResponse(
            feature.Id,
            feature.Slug,
            feature.Title,
            feature.IsPublished,
            feature.DisplayOrder,
            feature.DeliveryStatus.ToString(),
            feature.CreatedAt,
            feature.UpdatedAt));
    }
}
