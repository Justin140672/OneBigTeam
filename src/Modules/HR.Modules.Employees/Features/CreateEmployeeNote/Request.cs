using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.CreateEmployeeNote;

internal sealed record CreateEmployeeNoteRequest(
    Guid CompanyId,
    Guid EmployeeId,
    NoteCategory Category,
    string NoteText,
    bool IsImportant);
