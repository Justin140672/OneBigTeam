namespace HR.Web.Models;

public sealed record BackfillCandidatePreviewModel(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    DateOnly StartDate,
    string PredictedEmployeeNumber);

public sealed record PreviewBackfillEmployeeNumbersResponse(
    IReadOnlyList<BackfillCandidatePreviewModel> Candidates);

public sealed record BackfillResultItemModel(Guid EmployeeId, string AssignedEmployeeNumber);

public sealed record CommitBackfillEmployeeNumbersResponse(
    Guid BackfillOperationId,
    IReadOnlyList<BackfillResultItemModel> Items,
    int TotalCount);
