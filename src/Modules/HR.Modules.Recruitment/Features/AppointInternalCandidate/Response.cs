namespace HR.Modules.Recruitment.Features.AppointInternalCandidate;

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
