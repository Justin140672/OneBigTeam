using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Tests;

/// <summary>
/// SICK-03: SicknessRecord.ReopenFollowingUnfitReview domain tests. See the method's XML
/// remarks for the design rationale (a "not fit" return-to-work outcome reopens the existing
/// record rather than requiring a brand-new one).
/// </summary>
public class SicknessRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();

    private static SicknessRecord CreateClosedRecord() =>
        SicknessRecord.Create(
            Guid.NewGuid(), CompanyId, EmployeeId, CategoryId,
            new DateOnly(2026, 6, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 6, 5), SicknessDayPart.FullDay,
            totalDays: 5m, notes: null,
            evidenceStatus: SicknessEvidenceStatus.NotRequired, now: Now);

    private static SicknessRecord CreateActiveRecord() =>
        SicknessRecord.Create(
            Guid.NewGuid(), CompanyId, EmployeeId, CategoryId,
            new DateOnly(2026, 6, 1), SicknessDayPart.FullDay,
            endDate: null, endDayPart: null,
            totalDays: null, notes: null,
            evidenceStatus: SicknessEvidenceStatus.NotRequired, now: Now);

    [Fact]
    public void ReopenFollowingUnfitReview_OnClosedRecord_SetsActiveAndClearsCloseFields()
    {
        var record = CreateClosedRecord();
        var reopenedAt = Now.AddDays(30);

        record.ReopenFollowingUnfitReview(reopenedAt);

        Assert.Equal(SicknessStatus.Active, record.Status);
        Assert.Null(record.EndDate);
        Assert.Null(record.EndDayPart);
        Assert.Null(record.ReturnToWorkDate);
        Assert.Null(record.TotalDays);
        Assert.Equal(reopenedAt, record.UpdatedAt);
    }

    [Fact]
    public void ReopenFollowingUnfitReview_OnAlreadyActiveRecord_IsNoOp()
    {
        var record = CreateActiveRecord();
        var originalUpdatedAt = record.UpdatedAt;

        record.ReopenFollowingUnfitReview(Now.AddDays(30));

        Assert.Equal(SicknessStatus.Active, record.Status);
        Assert.Equal(originalUpdatedAt, record.UpdatedAt);
    }
}
