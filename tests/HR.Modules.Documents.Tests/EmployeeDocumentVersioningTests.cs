using HR.Modules.Documents.Domain;

namespace HR.Modules.Documents.Tests;

public class EmployeeDocumentVersioningTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Without_PreviousVersionId_Defaults_IsLatestVersion_True_And_PreviousVersionId_Null()
    {
        var doc = EmployeeDocument.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            CreatedAt);

        Assert.True(doc.IsLatestVersion);
        Assert.Null(doc.PreviousVersionId);
    }

    [Fact]
    public void Create_With_PreviousVersionId_Sets_It_And_Still_Marks_IsLatestVersion_True()
    {
        var previousId = Guid.NewGuid();

        var doc = EmployeeDocument.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            CreatedAt, previousVersionId: previousId);

        Assert.Equal(previousId, doc.PreviousVersionId);
        Assert.True(doc.IsLatestVersion);
    }

    [Fact]
    public void SupersedeAsPreviousVersion_Sets_IsLatestVersion_False_And_Bumps_UpdatedAt()
    {
        var doc = EmployeeDocument.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            CreatedAt);
        var now = CreatedAt.AddDays(3);

        doc.SupersedeAsPreviousVersion(now);

        Assert.False(doc.IsLatestVersion);
        Assert.Equal(now, doc.UpdatedAt);
    }

    [Fact]
    public void SupersedeAsPreviousVersion_Does_Not_Touch_IsArchived_Or_ExpiryDate_Or_Other_Fields()
    {
        var expiryDate = new DateOnly(2027, 5, 1);
        var issueDate  = new DateOnly(2026, 5, 1);
        var doc = EmployeeDocument.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            CreatedAt, issueDate: issueDate, expiryDate: expiryDate);
        var archivedBy = Guid.NewGuid();
        doc.Archive(archivedBy, "Some reason", CreatedAt.AddDays(1));

        doc.SupersedeAsPreviousVersion(CreatedAt.AddDays(2));

        Assert.True(doc.IsArchived);
        Assert.Equal(archivedBy, doc.ArchivedByUserId);
        Assert.Equal("Some reason", doc.ArchiveReason);
        Assert.Equal(expiryDate, doc.ExpiryDate);
        Assert.Equal(issueDate, doc.IssueDate);
    }
}
