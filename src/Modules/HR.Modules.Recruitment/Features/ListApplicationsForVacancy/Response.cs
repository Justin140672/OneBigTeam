using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.ListApplicationsForVacancy;

internal sealed record ListApplicationsForVacancyResponse(IReadOnlyList<ApplicationListItem> Items);

internal sealed record ApplicationListItem(
    Guid Id,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    string CandidateEmail,
    Guid CurrentStageId,
    InterviewOutcome? InterviewOutcome,
    bool IsWithdrawn,
    DateTimeOffset AppliedAt,
    // Ticket 2: offer response lifecycle for this application (null until an offer is made).
    string? OfferResponseStatus = null,
    decimal? OfferedSalary = null,
    DateOnly? OfferedStartDate = null,
    // Internal recruitment Ticket 6: true only when Source == Internal. EmployeeId is populated only
    // for internal applications (never for external candidates, even after they are hired).
    bool IsInternal = false,
    Guid? EmployeeId = null,
    // Internal recruitment Ticket 7: internal appointment progress ("Pending" / "Completed"; null when
    // none started) and, once completed, the effective date of the employee change.
    string? InternalAppointmentStatus = null,
    DateOnly? InternalAppointmentEffectiveDate = null,
    bool CurrentStageHasPendingInterview = false,
    Guid? PendingInterviewId = null,
    string? CurrentStageInterviewOutcome = null,
    bool HasNextInterviewStage = false,
    Guid? NextInterviewStageId = null,
    string? NextInterviewStageName = null,
    bool AllRequiredInterviewStagesPassed = true);
