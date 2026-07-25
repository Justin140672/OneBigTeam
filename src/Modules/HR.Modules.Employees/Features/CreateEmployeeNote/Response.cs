namespace HR.Modules.Employees.Features.CreateEmployeeNote;

internal sealed record CreateEmployeeNoteResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string Category,
    string NoteText,
    bool IsImportant,
    bool IsSuperseded,
    Guid? SupersededByNoteId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedDate);
