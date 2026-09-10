namespace HR.Modules.Recruitment.Features.SaveCvReviewNotes;

internal sealed record SaveCvReviewNotesRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }
    public string? CvReviewNotes { get; init; }
}
