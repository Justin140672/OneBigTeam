using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;

namespace HR.Modules.Employees.Tests;

public class EmployeeLeavingProcessTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 24, 10, 0, 0, TimeSpan.Zero);

    private static EmployeeLeavingProcess CreateInProgress(DateTimeOffset now) =>
        EmployeeLeavingProcess.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 31),
            NoticePeriodUnit.Weeks, 4, NoticePeriodSource.Employee, LeavingReason.Resignation,
            Guid.NewGuid(), now);

    [Fact]
    public void Amend_Updates_LeavingDate_LastWorkingDay_Reason_And_UpdatedAt()
    {
        var leavingProcess = CreateInProgress(FixedNow);
        var later = FixedNow.AddDays(1);

        leavingProcess.Amend(new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 31), LeavingReason.MutualAgreement, later);

        Assert.Equal(new DateOnly(2026, 9, 1), leavingProcess.LeavingDate);
        Assert.Equal(new DateOnly(2026, 8, 31), leavingProcess.LastWorkingDay);
        Assert.Equal(LeavingReason.MutualAgreement, leavingProcess.LeavingReason);
        Assert.Equal(later, leavingProcess.UpdatedAt);
    }

    [Fact]
    public void Amend_Leaves_NoticePeriod_Fields_And_Status_Untouched()
    {
        var leavingProcess = CreateInProgress(FixedNow);

        leavingProcess.Amend(new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 31), LeavingReason.MutualAgreement, FixedNow.AddDays(1));

        Assert.Equal(NoticePeriodUnit.Weeks, leavingProcess.NoticePeriodUnit);
        Assert.Equal(4, leavingProcess.NoticePeriodLength);
        Assert.Equal(NoticePeriodSource.Employee, leavingProcess.NoticeSource);
        Assert.Equal(LeavingProcessStatus.InProgress, leavingProcess.Status);
    }

    // Spec SPEC-OFF-01
    [Fact]
    public void Amend_Sets_Notes_When_Provided()
    {
        var leavingProcess = CreateInProgress(FixedNow);

        leavingProcess.Amend(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 31), LeavingReason.Other, FixedNow.AddDays(1),
            notes: "Emigrating overseas.");

        Assert.Equal("Emigrating overseas.", leavingProcess.Notes);
        Assert.Equal(NoticePeriodUnit.Weeks, leavingProcess.NoticePeriodUnit);
        Assert.Equal(4, leavingProcess.NoticePeriodLength);
        Assert.Equal(NoticePeriodSource.Employee, leavingProcess.NoticeSource);
    }

    [Fact]
    public void Amend_Clears_Notes_When_Not_Provided()
    {
        var leavingProcess = CreateInProgress(FixedNow);
        leavingProcess.Amend(
            new DateOnly(2026, 8, 15), new DateOnly(2026, 8, 14), LeavingReason.Other, FixedNow.AddDays(1),
            notes: "Original notes.");

        leavingProcess.Amend(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 31), LeavingReason.MutualAgreement, FixedNow.AddDays(2));

        Assert.Null(leavingProcess.Notes);
    }

    [Fact]
    public void Amend_Throws_When_Status_Is_Not_InProgress()
    {
        var leavingProcess = CreateInProgress(FixedNow);
        leavingProcess.Cancel("Retracted.", FixedNow.AddDays(1));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            leavingProcess.Amend(new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 31), LeavingReason.MutualAgreement, FixedNow.AddDays(2)));

        Assert.Equal("Cannot amend a leaving process with status 'Cancelled'.", ex.Message);
    }

    [Fact]
    public void Cancel_Sets_Status_CancelledAt_CancellationReason_And_UpdatedAt()
    {
        var leavingProcess = CreateInProgress(FixedNow);
        var later = FixedNow.AddDays(3);

        leavingProcess.Cancel("Employee retracted resignation.", later);

        Assert.Equal(LeavingProcessStatus.Cancelled, leavingProcess.Status);
        Assert.Equal(later, leavingProcess.CancelledAt);
        Assert.Equal("Employee retracted resignation.", leavingProcess.CancellationReason);
        Assert.Equal(later, leavingProcess.UpdatedAt);
    }

    [Fact]
    public void Cancel_Throws_When_Status_Is_Not_InProgress()
    {
        var leavingProcess = CreateInProgress(FixedNow);
        leavingProcess.Cancel("First cancellation.", FixedNow.AddDays(1));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            leavingProcess.Cancel("Second cancellation.", FixedNow.AddDays(2)));

        Assert.Equal("Cannot cancel a leaving process with status 'Cancelled'.", ex.Message);
    }

    [Fact]
    public void Complete_Sets_Status_And_UpdatedAt()
    {
        var leavingProcess = CreateInProgress(FixedNow);
        var later = FixedNow.AddDays(30);

        leavingProcess.Complete(later);

        Assert.Equal(LeavingProcessStatus.Completed, leavingProcess.Status);
        Assert.Equal(later, leavingProcess.UpdatedAt);
    }

    [Fact]
    public void Complete_Throws_When_Status_Is_Not_InProgress()
    {
        var leavingProcess = CreateInProgress(FixedNow);
        leavingProcess.Cancel("Retracted.", FixedNow.AddDays(1));

        var ex = Assert.Throws<InvalidOperationException>(() => leavingProcess.Complete(FixedNow.AddDays(2)));

        Assert.Equal("Cannot complete a leaving process with status 'Cancelled'.", ex.Message);
    }

    [Fact]
    public void MarkFinalisationCompleted_Throws_When_Status_Is_Not_Completed()
    {
        var leavingProcess = CreateInProgress(FixedNow);

        var ex = Assert.Throws<InvalidOperationException>(() => leavingProcess.MarkFinalisationCompleted(FixedNow.AddDays(1)));

        Assert.Equal("Cannot mark finalisation completed for a leaving process with status 'InProgress'.", ex.Message);
        Assert.Null(leavingProcess.FinalisationCompletedAt);
    }

    [Fact]
    public void MarkFinalisationCompleted_Sets_FinalisationCompletedAt_And_UpdatedAt_When_Completed()
    {
        var leavingProcess = CreateInProgress(FixedNow);
        leavingProcess.Complete(FixedNow.AddDays(30));
        var later = FixedNow.AddDays(31);

        leavingProcess.MarkFinalisationCompleted(later);

        Assert.Equal(later, leavingProcess.FinalisationCompletedAt);
        Assert.Equal(later, leavingProcess.UpdatedAt);
    }

    [Fact]
    public void MarkFinalisationCompleted_Is_A_No_Op_When_Called_A_Second_Time()
    {
        var leavingProcess = CreateInProgress(FixedNow);
        leavingProcess.Complete(FixedNow.AddDays(30));
        var firstCompletion = FixedNow.AddDays(31);
        leavingProcess.MarkFinalisationCompleted(firstCompletion);

        var exception = Record.Exception(() => leavingProcess.MarkFinalisationCompleted(FixedNow.AddDays(32)));

        Assert.Null(exception);
        Assert.Equal(firstCompletion, leavingProcess.FinalisationCompletedAt);
    }
}
