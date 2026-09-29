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
