using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.CreateApplication;

internal sealed record CreateApplicationRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid CandidateId { get; init; }
    public string? Notes { get; init; }

    public ApplicationSource? Source { get; init; }
    public Guid? SourceExternalRecruiterId { get; init; }

    // Internal recruitment Ticket 1: optional id of the CandidateDocument (Kind = Cv) submitted with
    // this application. Must belong to the same company and candidate (enforced in the handler and by
    // the database). Omitted/null leaves the application with no captured CV.
    public Guid? CvDocumentId { get; init; }

    internal Guid? PerformedByUserId { get; init; }
}
