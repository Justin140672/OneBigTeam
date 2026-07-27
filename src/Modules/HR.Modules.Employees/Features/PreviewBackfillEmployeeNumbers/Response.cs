namespace HR.Modules.Employees.Features.PreviewBackfillEmployeeNumbers;

internal sealed record BackfillCandidatePreview(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    DateOnly StartDate,
    string PredictedEmployeeNumber);

internal sealed record PreviewBackfillEmployeeNumbersResponse(
    IReadOnlyList<BackfillCandidatePreview> Candidates);
