using FastEndpoints;
using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.SupersedeEmployeeNote;

internal sealed record SupersedeEmployeeNoteRequest(
    Guid CompanyId,
    Guid EmployeeId,
    [property: BindFrom("noteId")] Guid OriginalNoteId,
    NoteCategory Category,
    string NoteText,
    bool IsImportant);
