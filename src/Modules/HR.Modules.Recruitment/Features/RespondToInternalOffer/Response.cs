namespace HR.Modules.Recruitment.Features.RespondToInternalOffer;

internal sealed record RespondToInternalOfferResponse(
    Guid ApplicationId,
    Guid VacancyId,
    int OfferVersion,
    string OfferResponseStatus,
    DateTimeOffset? OfferRespondedAt,
    bool WasAlreadyRecorded);
