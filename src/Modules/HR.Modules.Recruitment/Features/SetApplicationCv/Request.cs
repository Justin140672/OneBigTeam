namespace HR.Modules.Recruitment.Features.SetApplicationCv;

internal sealed record SetApplicationCvRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }

    public Guid? CvDocumentId { get; init; }

    // Ticket 2 (optimistic concurrency): the application Version the caller loaded (returned by
    // GetApplication). Mandatory — two simultaneous CV changes must not silently overwrite each other.
    public int? ExpectedVersion { get; init; }
}
