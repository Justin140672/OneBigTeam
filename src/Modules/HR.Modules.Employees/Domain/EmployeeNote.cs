namespace HR.Modules.Employees.Domain;

internal sealed class EmployeeNote
{
    private EmployeeNote() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public NoteCategory Category { get; private set; }
    public string NoteText { get; private set; } = string.Empty;
    public bool IsImportant { get; private set; }
    public bool IsSuperseded { get; private set; }
    public Guid? SupersededByNoteId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }

    public static EmployeeNote Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        NoteCategory category,
        string noteText,
        bool isImportant,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        return new EmployeeNote
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            Category = category,
            NoteText = noteText,
            IsImportant = isImportant,
            IsSuperseded = false,
            SupersededByNoteId = null,
            CreatedByUserId = createdByUserId,
            CreatedDate = now
        };
    }

    public void MarkSuperseded(Guid supersedingNoteId)
    {
        IsSuperseded = true;
        SupersededByNoteId = supersedingNoteId;
    }
}
