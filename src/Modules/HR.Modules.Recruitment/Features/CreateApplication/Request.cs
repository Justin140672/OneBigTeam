using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.CreateApplication;

internal sealed record CreateApplicationRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid CandidateId { get; init; }
    public string? Notes { get; init; }

    // Ticket #78. Both optional for backward compatibility with existing callers that don't yet
    // record a source. SourceExternalRecruiterId is required if and only if Source == ExternalRecruiter
    // (enforced in CreateApplicationValidator).
    public ApplicationSource? Source { get; init; }
    public Guid? SourceExternalRecruiterId { get; init; }

    // Internal recruitment Ticket 1: optional id of the CandidateDocument (Kind = Cv) submitted with
    // this application. Must belong to the same company and candidate (enforced in the handler and by
    // the database). Omitted/null leaves the application with no captured CV.
    public Guid? CvDocumentId { get; init; }

    // Populated by the endpoint from ICurrentUser (never bound from the client) so the CV-reference
    // audit event carries an actor, as AUD-04 requires for human-triggered events.
    internal Guid? PerformedByUserId { get; init; }
}
