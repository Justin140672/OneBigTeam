namespace HR.Modules.Probation.Features.GetProbationStatus;

// Deliberately minimal — used to decide whether the Employee Overview page should show its
// Probation tab at all, so it must not pull in review history the way GetProbationRecordByEmployee
// does.
internal sealed record GetProbationStatusResponse(bool HasRecord, string? Status);
