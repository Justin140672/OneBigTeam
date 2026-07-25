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
}
