namespace HR.Modules.Employees.Features.GetEmployeeNotes;

internal sealed record EmployeeNoteItem(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string Category,
    string NoteText,
    bool IsImportant,
    bool IsSuperseded,
    Guid? SupersededByNoteId,
    Guid CreatedByUserId,
    string CreatedByName,
    DateTimeOffset CreatedDate);

internal sealed record GetEmployeeNotesResponse(IReadOnlyList<EmployeeNoteItem> Items);
