namespace HR.Modules.Marketing.Features.SetMarketingFeaturePublication;

internal sealed record SetMarketingFeaturePublicationRequest(
    Guid Id,
    bool IsPublished);
