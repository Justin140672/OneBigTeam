namespace HR.Modules.Employees.Features.SupersedeEmployeeNote;

internal sealed record SupersedeEmployeeNoteResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string Category,
    string NoteText,
    bool IsImportant,
    bool IsSuperseded,
    Guid? SupersededByNoteId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedDate,
    Guid OriginalNoteId,
    bool OriginalNoteSuperseded);
