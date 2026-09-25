namespace HR.Modules.Recruitment.Features.SetApplicationCv;

internal sealed record SetApplicationCvRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }

    // The CandidateDocument (Kind = Cv, same company and candidate) to record as the CV submitted with
    // this application, attaching or replacing any existing reference. Null removes the reference —
    // for example so that the previously referenced document can then be deleted.
    public Guid? CvDocumentId { get; init; }

    // Ticket 2 (optimistic concurrency): the application Version the caller loaded (returned by
    // GetApplication). Mandatory — two simultaneous CV changes must not silently overwrite each other.
    public int? ExpectedVersion { get; init; }
}
