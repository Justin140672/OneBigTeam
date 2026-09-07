using HR.Modules.Marketing.Domain;
using HR.Modules.Marketing.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.GetMarketingContent;

internal sealed class GetMarketingContentHandler(MarketingDbContext dbContext)
{
    public async Task<Result<GetMarketingContentResponse>> HandleAsync(
        GetMarketingContentRequest request,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.MarketingProducts
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == MarketingProduct.SingletonId, cancellationToken);

        var features = await dbContext.MarketingFeatures
            .AsNoTracking()
            .Where(f => f.IsPublished)
            .OrderBy(f => f.DisplayOrder)
            .ThenBy(f => f.Title)
            .ToListAsync(cancellationToken);

        var roadmap = await dbContext.MarketingRoadmapItems
            .AsNoTracking()
            .Where(r => r.IsPublished)
            .OrderBy(r => r.DisplayOrder)
            .ThenBy(r => r.Title)
            .ToListAsync(cancellationToken);

        var response = new GetMarketingContentResponse(
            new MarketingContentProductDto(product?.Name ?? "One Big Team", product?.Tagline),
            features.Select(f => new MarketingContentFeatureDto(
                f.Slug,
                f.IconName,
                f.Title,
                f.Summary,
                f.Intro,
                f.DetailedContent,
                f.Benefits,
                f.YouTubeId,
                f.DeliveryStatus.ToString())).ToList(),
            roadmap.Select(r => new MarketingContentRoadmapDto(
                r.Title,
                r.Description,
                r.IconName,
                r.DeliveryStatus.ToString())).ToList());

        return Result.Success(response);
    }
}
