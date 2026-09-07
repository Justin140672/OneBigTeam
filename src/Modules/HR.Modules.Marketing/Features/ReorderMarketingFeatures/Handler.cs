using HR.SharedKernel;
using HR.Modules.Marketing.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.ReorderMarketingFeatures;

internal sealed class ReorderMarketingFeaturesHandler(
    MarketingDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<ReorderMarketingFeaturesResponse>> HandleAsync(
        ReorderMarketingFeaturesRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var orderedIds = request.OrderedIds;

        var features = await dbContext.MarketingFeatures
            .Where(f => orderedIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        if (features.Count != orderedIds.Count)
        {
            return Result.Failure<ReorderMarketingFeaturesResponse>(
                Error.NotFound("One or more marketing features could not be found."));
        }

        var previousOrder = features
            .OrderBy(f => f.DisplayOrder)
            .Select(f => f.Id)
            .ToList();

        var byId = features.ToDictionary(f => f.Id);
        for (var index = 0; index < orderedIds.Count; index++)
        {
            byId[orderedIds[index]].SetDisplayOrder(index, currentUser.UserId, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new MarketingFeaturesReorderedAuditEvent(
                currentUser.UserId,
                now,
                previousOrder,
                orderedIds.ToList()),
            cancellationToken);

        return Result.Success(new ReorderMarketingFeaturesResponse(
            orderedIds.Select((id, index) => new ReorderedMarketingFeatureDto(id, index)).ToList()));
    }
}
