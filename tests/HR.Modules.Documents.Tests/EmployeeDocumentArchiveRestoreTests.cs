using HR.Modules.Documents.Domain;

namespace HR.Modules.Documents.Tests;

// DOC-04: domain-level coverage of EmployeeDocument.Archive/Restore, independent of the handlers
// that call them (DeleteEmployeeDocumentHandlerTests / RestoreEmployeeDocumentHandlerTests already
// exercise these indirectly, but the guard/state-transition behaviour deserves a direct test).
public class EmployeeDocumentArchiveRestoreTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private static EmployeeDocument CreateEmployeeDocument() =>
        EmployeeDocument.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CreatedAt);

    [Fact]
    public void Archive_Sets_IsArchived_And_Records_Actor_Timestamp_Reason()
    {
        var empDoc = CreateEmployeeDocument();
        var archivedBy = Guid.NewGuid();
        var now = CreatedAt.AddDays(10);

        empDoc.Archive(archivedBy, "No longer required", now);

        Assert.True(empDoc.IsArchived);
        Assert.Equal(archivedBy, empDoc.ArchivedByUserId);
        Assert.Equal(now, empDoc.ArchivedAt);
        Assert.Equal("No longer required", empDoc.ArchiveReason);
        Assert.Equal(now, empDoc.UpdatedAt);
    }

    [Fact]
    public void Archive_Trims_Reason()
    {
        var empDoc = CreateEmployeeDocument();

        empDoc.Archive(Guid.NewGuid(), "  Superseded by new copy  ", CreatedAt.AddDays(1));

        Assert.Equal("Superseded by new copy", empDoc.ArchiveReason);
    }

    [Fact]
    public void Archive_With_Null_Reason_Leaves_ArchiveReason_Null()
    {
        var empDoc = CreateEmployeeDocument();

        empDoc.Archive(Guid.NewGuid(), null, CreatedAt.AddDays(1));

        Assert.Null(empDoc.ArchiveReason);
    }

    [Fact]
    public void Archive_With_Empty_String_Reason_Stores_Null()
    {
        var empDoc = CreateEmployeeDocument();

        empDoc.Archive(Guid.NewGuid(), string.Empty, CreatedAt.AddDays(1));

        Assert.Null(empDoc.ArchiveReason);
    }

    [Fact]
    public void Archive_With_Whitespace_Only_Reason_Stores_Null()
    {
        var empDoc = CreateEmployeeDocument();

        empDoc.Archive(Guid.NewGuid(), "   ", CreatedAt.AddDays(1));

        Assert.Null(empDoc.ArchiveReason);
    }

    [Fact]
    public void Restore_Clears_Archive_Fields_And_Records_Restorer()
    {
        var empDoc = CreateEmployeeDocument();
        var archivedBy = Guid.NewGuid();
        var restoredBy = Guid.NewGuid();
        var archivedAt = CreatedAt.AddDays(5);
        var restoredAt = CreatedAt.AddDays(20);

        empDoc.Archive(archivedBy, "Temporary removal", archivedAt);
        empDoc.Restore(restoredBy, restoredAt);

        Assert.False(empDoc.IsArchived);
        Assert.Null(empDoc.ArchivedByUserId);
        Assert.Null(empDoc.ArchivedAt);
        Assert.Null(empDoc.ArchiveReason);
        Assert.Equal(restoredBy, empDoc.RestoredByUserId);
        Assert.Equal(restoredAt, empDoc.RestoredAt);
        Assert.Equal(restoredAt, empDoc.UpdatedAt);
    }

    [Fact]
    public void Restore_On_Never_Archived_Document_Still_Sets_Restored_Fields()
    {
        // Restore has no explicit guard against being called on a non-archived document — the
        // handler is responsible for enforcing that invariant (RestoreEmployeeDocumentHandler
        // returns Conflict before calling Restore). This test pins the domain method's own
        // unconditional behaviour so a future change to that contract is caught here.
        var empDoc = CreateEmployeeDocument();
        var restoredBy = Guid.NewGuid();
        var restoredAt = CreatedAt.AddDays(1);

        empDoc.Restore(restoredBy, restoredAt);

        Assert.False(empDoc.IsArchived);
        Assert.Equal(restoredBy, empDoc.RestoredByUserId);
        Assert.Equal(restoredAt, empDoc.RestoredAt);
    }
}
