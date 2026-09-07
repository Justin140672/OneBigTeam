namespace HR.Modules.Marketing.Features.SetMarketingFeaturePublication;

internal sealed record SetMarketingFeaturePublicationResponse(
    Guid Id,
    bool IsPublished,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedByUserId);
