using HR.Modules.Marketing.Domain;
using HR.Modules.Marketing.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.ListMarketingContent;

internal sealed class ListMarketingContentHandler(MarketingDbContext dbContext)
{
    public async Task<Result<ListMarketingContentResponse>> HandleAsync(
        ListMarketingContentRequest request,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.MarketingProducts
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == MarketingProduct.SingletonId, cancellationToken);

        var features = await dbContext.MarketingFeatures
            .AsNoTracking()
            .OrderBy(f => f.DisplayOrder)
            .ThenBy(f => f.Title)
            .ToListAsync(cancellationToken);

        var roadmap = await dbContext.MarketingRoadmapItems
            .AsNoTracking()
            .OrderBy(r => r.DisplayOrder)
            .ThenBy(r => r.Title)
            .ToListAsync(cancellationToken);

        var productDto = product is null
            ? new AdminMarketingProductDto(MarketingProduct.SingletonId, "One Big Team", null, default, null)
            : new AdminMarketingProductDto(product.Id, product.Name, product.Tagline, product.UpdatedAt, product.UpdatedByUserId);

        var response = new ListMarketingContentResponse(
            productDto,
            features.Select(f => new AdminMarketingFeatureDto(
                f.Id,
                f.Slug,
                f.IconName,
                f.Title,
                f.Summary,
                f.Intro,
                f.DetailedContent,
                f.Benefits,
                f.YouTubeId,
                f.DisplayOrder,
                f.IsPublished,
                f.DeliveryStatus.ToString(),
                f.CreatedAt,
                f.CreatedByUserId,
                f.UpdatedAt,
                f.UpdatedByUserId)).ToList(),
            roadmap.Select(r => new AdminMarketingRoadmapDto(
                r.Id,
                r.Title,
                r.Description,
                r.IconName,
                r.DisplayOrder,
                r.IsPublished,
                r.DeliveryStatus.ToString(),
                r.CreatedAt,
                r.CreatedByUserId,
                r.UpdatedAt,
                r.UpdatedByUserId)).ToList());

        return Result.Success(response);
    }
}
