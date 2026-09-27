namespace HR.Modules.Recruitment.Features.AppointInternalCandidate;

/// <param name="IsApplied">False when the effective date is in the future: the change is scheduled
/// and the employee record is updated on that date.</param>
internal sealed record AppointInternalCandidateResponse(
    Guid ApplicationId,
    Guid VacancyId,
    Guid CandidateId,
    Guid EmployeeId,
    Guid PromotionId,
    Guid CurrentStageId,
    Guid PositionProfileId,
    Guid DepartmentId,
    Guid LocationId,
    Guid? ManagerId,
    DateOnly EffectiveDate,
    bool IsApplied,
    Guid? CompensationId,
    string AppointmentStatus);
