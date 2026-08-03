namespace HR.Web.Models;

public sealed record EmployeeNoteItemModel(
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

public sealed record GetEmployeeNotesResponse(IReadOnlyList<EmployeeNoteItemModel> Items);

// ── CREATE ────────────────────────────────────────────────────────────────────

public sealed record CreateEmployeeNoteRequest(
    Guid CompanyId,
    Guid EmployeeId,
    string Category,
    string NoteText,
    bool IsImportant);

public sealed record CreateEmployeeNoteResponse(
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

// ── SUPERSEDE ─────────────────────────────────────────────────────────────────

public sealed record SupersedeEmployeeNoteRequest(
    Guid CompanyId,
    Guid EmployeeId,
    string Category,
    string NoteText,
    bool IsImportant);

public sealed record SupersedeEmployeeNoteResponse(
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

// ── HELPERS ───────────────────────────────────────────────────────────────────

public static class EmployeeNoteCategories
{
    public static readonly string[] All =
    [
        "General",
        "Performance",
        "Attendance",
        "Conduct",
        "Wellbeing",
        "Recruitment",
        "Compensation",
        "Compliance",
        "Other"
    ];

    // Every current NoteCategory value is already a single, human-readable word — this switch
    // exists (matching the PeriodLabel/Reason label conventions elsewhere in this codebase) so a
    // future multi-word category only needs a mapping added here, not a new mechanism.
    public static string Label(string category) => category switch
    {
        "General" => "General",
        "Performance" => "Performance",
        "Attendance" => "Attendance",
        "Conduct" => "Conduct",
        "Wellbeing" => "Wellbeing",
        "Recruitment" => "Recruitment",
        "Compensation" => "Compensation",
        "Compliance" => "Compliance",
        "Other" => "Other",
        _ => category
    };
}
