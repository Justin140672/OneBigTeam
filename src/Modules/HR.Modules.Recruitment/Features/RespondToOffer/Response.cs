namespace HR.Modules.Recruitment.Features.RespondToOffer;

internal sealed record RespondToOfferResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid CurrentStageId,
    string OfferResponseStatus,
    decimal? OfferedSalary,
    string? OfferedSalaryFrequency,
    DateOnly? ProposedStartDate,
    DateOnly? OfferDate,
    string? OfferNotes,
    DateTimeOffset? OfferMadeAt,
    DateTimeOffset? OfferRespondedAt);
