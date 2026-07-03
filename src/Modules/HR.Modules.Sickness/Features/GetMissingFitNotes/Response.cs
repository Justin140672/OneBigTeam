namespace HR.Modules.Sickness.Features.GetMissingFitNotes;

internal sealed record GetMissingFitNotesResponse(IReadOnlyList<MissingFitNoteItem> Items);

internal sealed record MissingFitNoteItem(
    Guid RequestId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    DateOnly DueDate,
    string Status);
