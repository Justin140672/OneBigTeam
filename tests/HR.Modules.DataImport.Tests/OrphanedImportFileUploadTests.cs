using HR.Modules.DataImport.Domain;

namespace HR.Modules.DataImport.Tests;

public class OrphanedImportFileUploadTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreatePending_Sets_Expected_Initial_State()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        var orphan = OrphanedImportFileUpload.CreateReserved(id, companyId, "company/session/employees.xlsx", Now);

        Assert.Equal(id, orphan.Id);
        Assert.Equal(companyId, orphan.CompanyId);
        Assert.Equal("company/session/employees.xlsx", orphan.StorageKey);
        Assert.Equal(Now, orphan.CreatedAt);
        Assert.Null(orphan.DeletedAt);
        Assert.Null(orphan.LastAttemptedAt);
        Assert.Equal(0, orphan.AttemptCount);
    }

    [Fact]
    public void MarkDeleted_Sets_DeletedAt_And_LastAttemptedAt()
    {
        var orphan = OrphanedImportFileUpload.CreateReserved(Guid.NewGuid(), Guid.NewGuid(), "key", Now);

        orphan.MarkDeleted(Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(5), orphan.DeletedAt);
        Assert.Equal(Now.AddMinutes(5), orphan.LastAttemptedAt);
    }

    [Fact]
    public void MarkDeleted_Is_Idempotent_When_Called_A_Second_Time()
    {
        var orphan = OrphanedImportFileUpload.CreateReserved(Guid.NewGuid(), Guid.NewGuid(), "key", Now);

        orphan.MarkDeleted(Now.AddMinutes(5));
        // A second call (e.g. a retried sweep tick racing itself) must not throw or corrupt state.
        orphan.MarkDeleted(Now.AddMinutes(10));

        Assert.Equal(Now.AddMinutes(10), orphan.DeletedAt);
        Assert.Equal(Now.AddMinutes(10), orphan.LastAttemptedAt);
    }

    [Fact]
    public void RecordAttemptFailed_Increments_AttemptCount_And_Sets_LastAttemptedAt()
    {
        var orphan = OrphanedImportFileUpload.CreateReserved(Guid.NewGuid(), Guid.NewGuid(), "key", Now);

        orphan.RecordAttemptFailed(Now.AddMinutes(1));
        Assert.Equal(1, orphan.AttemptCount);
        Assert.Equal(Now.AddMinutes(1), orphan.LastAttemptedAt);

        orphan.RecordAttemptFailed(Now.AddMinutes(2));
        Assert.Equal(2, orphan.AttemptCount);
        Assert.Equal(Now.AddMinutes(2), orphan.LastAttemptedAt);
    }

    [Fact]
    public void RecordAttemptFailed_Does_Not_Set_DeletedAt()
    {
        var orphan = OrphanedImportFileUpload.CreateReserved(Guid.NewGuid(), Guid.NewGuid(), "key", Now);

        orphan.RecordAttemptFailed(Now.AddMinutes(1));

        Assert.Null(orphan.DeletedAt);
    }
}
