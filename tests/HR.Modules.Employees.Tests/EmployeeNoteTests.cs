using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Tests;

public class EmployeeNoteTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_All_Fields()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();

        var note = EmployeeNote.Create(
            id, companyId, employeeId, NoteCategory.Performance, "Great quarter.", true, createdByUserId, Now);

        Assert.Equal(id, note.Id);
        Assert.Equal(companyId, note.CompanyId);
        Assert.Equal(employeeId, note.EmployeeId);
        Assert.Equal(NoteCategory.Performance, note.Category);
        Assert.Equal("Great quarter.", note.NoteText);
        Assert.True(note.IsImportant);
        Assert.Equal(createdByUserId, note.CreatedByUserId);
        Assert.Equal(Now, note.CreatedDate);
        Assert.False(note.IsSuperseded);
        Assert.Null(note.SupersededByNoteId);
    }

    [Fact]
    public void Create_Sets_IsImportant_False_When_Specified()
    {
        var note = EmployeeNote.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NoteCategory.General, "Routine note.", false, Guid.NewGuid(), Now);

        Assert.False(note.IsImportant);
    }

    [Fact]
    public void MarkSuperseded_Sets_IsSuperseded_And_SupersededByNoteId()
    {
        var note = EmployeeNote.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NoteCategory.Conduct, "Original text.", true, Guid.NewGuid(), Now);
        var supersedingId = Guid.NewGuid();

        note.MarkSuperseded(supersedingId);

        Assert.True(note.IsSuperseded);
        Assert.Equal(supersedingId, note.SupersededByNoteId);
    }

    [Fact]
    public void MarkSuperseded_Does_Not_Alter_Other_Fields()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();
        var note = EmployeeNote.Create(
            Guid.NewGuid(), companyId, employeeId, NoteCategory.Wellbeing, "Check-in notes.", true, createdByUserId, Now);

        note.MarkSuperseded(Guid.NewGuid());

        Assert.Equal(companyId, note.CompanyId);
        Assert.Equal(employeeId, note.EmployeeId);
        Assert.Equal(NoteCategory.Wellbeing, note.Category);
        Assert.Equal("Check-in notes.", note.NoteText);
        Assert.True(note.IsImportant);
        Assert.Equal(createdByUserId, note.CreatedByUserId);
        Assert.Equal(Now, note.CreatedDate);
    }
}
