namespace HR.Modules.Recruitment.Features.ApproveOffer;

internal sealed record ApproveOfferResponse(
    Guid ApplicationId,
    Guid CompanyId,
    DateTimeOffset OfferApprovedAt,
    Guid OfferApprovedByUserId);
