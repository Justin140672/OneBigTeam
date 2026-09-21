namespace HR.Modules.Sickness.Features.GetMissingFitNotes;

internal sealed record GetMissingFitNotesResponse(IReadOnlyList<MissingFitNoteItem> Items);

internal sealed record MissingFitNoteItem(
    Guid RequestId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    DateOnly DueDate,
    string Status,
    // The open Tasks-module task id for this evidence request (TaskActionType.Upload, keyed by
    // RequestId), when one exists. Lets the dashboard open the exact Task View entry instead of
    // falling back to the employee profile.
    Guid? TaskId = null);
