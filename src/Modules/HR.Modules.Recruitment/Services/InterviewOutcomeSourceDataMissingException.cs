namespace HR.Modules.Recruitment.Services;

/// <summary>
/// The interview or application needed to build the interview-outcome audit event no longer exists.
/// Retrying cannot repair this, so reconciliation treats it as a terminal data-integrity failure.
/// </summary>
internal sealed class InterviewOutcomeSourceDataMissingException(Guid companyId, Guid interviewId, Guid applicationId)
    : Exception($"Interview {interviewId} (application {applicationId}, company {companyId}) or its application could not be loaded, so the interview-outcome audit event cannot be created.")
{
    public Guid CompanyId { get; } = companyId;
    public Guid InterviewId { get; } = interviewId;
    public Guid ApplicationId { get; } = applicationId;
}
