namespace HR.Modules.Recruitment.Features.SetApplicationCv;

internal sealed record SetApplicationCvResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid? CvDocumentId,
    int Version,
    DateTimeOffset UpdatedAt);
