using HR.Modules.Probation.Domain;

namespace HR.Modules.Probation.Tests;

public class ProbationRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 6, 1);
    private static readonly DateOnly ExpectedEndDate = new(2026, 9, 1);

    private static ProbationRecord CreateActiveRecord() =>
        ProbationRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            StartDate, ExpectedEndDate, null, Now);

    // -------- Status transition table --------

    [Fact]
    public void Active_Can_Transition_To_ReviewDue()
    {
        var record = CreateActiveRecord();
        record.MarkReviewDue(Now);
        Assert.Equal(ProbationStatus.ReviewDue, record.Status);
    }

    [Fact]
    public void Active_Can_Transition_To_Extended()
    {
        var record = CreateActiveRecord();
        record.Extend(ExpectedEndDate.AddMonths(1), "Needs more time.", Guid.NewGuid(), ExpectedEndDate.AddDays(-1), Now);
        Assert.Equal(ProbationStatus.Extended, record.Status);
    }

    [Fact]
    public void Active_Can_Transition_To_Passed()
    {
        var record = CreateActiveRecord();
        record.Pass(Guid.NewGuid(), ExpectedEndDate, null, Now);
        Assert.Equal(ProbationStatus.Passed, record.Status);
    }

    [Fact]
    public void Active_Can_Transition_To_Failed()
    {
        var record = CreateActiveRecord();
        record.Fail(Guid.NewGuid(), ExpectedEndDate, null, Now);
        Assert.Equal(ProbationStatus.Failed, record.Status);
    }

    [Fact]
    public void ReviewDue_Can_Transition_To_Extended()
    {
        var record = CreateActiveRecord();
        record.MarkReviewDue(Now);
        record.Extend(ExpectedEndDate.AddMonths(1), "Needs more time.", Guid.NewGuid(), ExpectedEndDate.AddDays(-1), Now);
        Assert.Equal(ProbationStatus.Extended, record.Status);
    }

    [Fact]
    public void ReviewDue_Can_Transition_To_Passed()
    {
        var record = CreateActiveRecord();
        record.MarkReviewDue(Now);
        record.Pass(Guid.NewGuid(), ExpectedEndDate, null, Now);
        Assert.Equal(ProbationStatus.Passed, record.Status);
    }

    [Fact]
    public void ReviewDue_Can_Transition_To_Failed()
    {
        var record = CreateActiveRecord();
        record.MarkReviewDue(Now);
        record.Fail(Guid.NewGuid(), ExpectedEndDate, null, Now);
        Assert.Equal(ProbationStatus.Failed, record.Status);
    }

    [Fact]
    public void ReviewDue_Cannot_Transition_To_ReviewDue_Again()
    {
        var record = CreateActiveRecord();
        record.MarkReviewDue(Now);

        Assert.Throws<InvalidOperationException>(() => record.MarkReviewDue(Now));
    }

    [Fact]
    public void Extended_Can_Transition_To_ReviewDue()
    {
        var record = CreateActiveRecord();
        record.Extend(ExpectedEndDate.AddMonths(1), "Needs more time.", Guid.NewGuid(), ExpectedEndDate.AddDays(-1), Now);
        record.MarkReviewDue(Now);
        Assert.Equal(ProbationStatus.ReviewDue, record.Status);
    }

    [Fact]
    public void Extended_Can_Transition_To_Extended_Again()
    {
        var record = CreateActiveRecord();
        var firstEnd = ExpectedEndDate.AddMonths(1);
        record.Extend(firstEnd, "First extension.", Guid.NewGuid(), ExpectedEndDate.AddDays(-1), Now);

        record.Extend(firstEnd.AddMonths(1), "Second extension.", Guid.NewGuid(), firstEnd.AddDays(-1), Now);

        Assert.Equal(ProbationStatus.Extended, record.Status);
        Assert.Equal(firstEnd.AddMonths(1), record.ExpectedEndDate);
    }

    [Fact]
    public void Extended_Can_Transition_To_Passed()
    {
        var record = CreateActiveRecord();
        var newEnd = ExpectedEndDate.AddMonths(1);
        record.Extend(newEnd, "Needs more time.", Guid.NewGuid(), ExpectedEndDate.AddDays(-1), Now);

        record.Pass(Guid.NewGuid(), newEnd, null, Now);

        Assert.Equal(ProbationStatus.Passed, record.Status);
    }

    [Fact]
    public void Extended_Can_Transition_To_Failed()
    {
        var record = CreateActiveRecord();
        var newEnd = ExpectedEndDate.AddMonths(1);
        record.Extend(newEnd, "Needs more time.", Guid.NewGuid(), ExpectedEndDate.AddDays(-1), Now);

        record.Fail(Guid.NewGuid(), newEnd, null, Now);

        Assert.Equal(ProbationStatus.Failed, record.Status);
    }

    [Fact]
    public void Passed_Cannot_Transition_To_ReviewDue()
    {
        var record = CreateActiveRecord();
        record.Pass(Guid.NewGuid(), ExpectedEndDate, null, Now);

        Assert.Throws<InvalidOperationException>(() => record.MarkReviewDue(Now));
    }

    [Fact]
    public void Passed_Cannot_Transition_To_Extended()
    {
        var record = CreateActiveRecord();
        record.Pass(Guid.NewGuid(), ExpectedEndDate, null, Now);

        Assert.Throws<InvalidOperationException>(() =>
            record.Extend(ExpectedEndDate.AddMonths(1), "Reason.", Guid.NewGuid(), ExpectedEndDate.AddDays(-1), Now));
    }

    [Fact]
    public void Passed_Cannot_Transition_To_Passed_Again()
    {
        var record = CreateActiveRecord();
        record.Pass(Guid.NewGuid(), ExpectedEndDate, null, Now);

        Assert.Throws<InvalidOperationException>(() => record.Pass(Guid.NewGuid(), ExpectedEndDate, null, Now));
    }

    [Fact]
    public void Passed_Cannot_Transition_To_Failed()
    {
        var record = CreateActiveRecord();
        record.Pass(Guid.NewGuid(), ExpectedEndDate, null, Now);

        Assert.Throws<InvalidOperationException>(() => record.Fail(Guid.NewGuid(), ExpectedEndDate, null, Now));
    }

    [Fact]
    public void Failed_Cannot_Transition_To_ReviewDue()
    {
        var record = CreateActiveRecord();
        record.Fail(Guid.NewGuid(), ExpectedEndDate, null, Now);

        Assert.Throws<InvalidOperationException>(() => record.MarkReviewDue(Now));
    }

    [Fact]
    public void Failed_Cannot_Transition_To_Extended()
    {
        var record = CreateActiveRecord();
        record.Fail(Guid.NewGuid(), ExpectedEndDate, null, Now);

        Assert.Throws<InvalidOperationException>(() =>
            record.Extend(ExpectedEndDate.AddMonths(1), "Reason.", Guid.NewGuid(), ExpectedEndDate.AddDays(-1), Now));
    }

    [Fact]
    public void Failed_Cannot_Transition_To_Passed()
    {
        var record = CreateActiveRecord();
        record.Fail(Guid.NewGuid(), ExpectedEndDate, null, Now);

        Assert.Throws<InvalidOperationException>(() => record.Pass(Guid.NewGuid(), ExpectedEndDate, null, Now));
    }

    [Fact]
    public void Failed_Cannot_Transition_To_Failed_Again()
    {
        var record = CreateActiveRecord();
        record.Fail(Guid.NewGuid(), ExpectedEndDate, null, Now);

        Assert.Throws<InvalidOperationException>(() => record.Fail(Guid.NewGuid(), ExpectedEndDate, null, Now));
    }

    // -------- Extend() date validation --------

    [Fact]
    public void Extend_With_NewEndDate_Equal_To_Current_ExpectedEndDate_Throws()
    {
        var record = CreateActiveRecord();

        Assert.Throws<InvalidOperationException>(() =>
            record.Extend(ExpectedEndDate, "Reason.", Guid.NewGuid(), ExpectedEndDate.AddDays(-1), Now));
    }

    [Fact]
    public void Extend_With_NewEndDate_Before_Current_ExpectedEndDate_Throws()
    {
        var record = CreateActiveRecord();

        Assert.Throws<InvalidOperationException>(() =>
            record.Extend(ExpectedEndDate.AddDays(-1), "Reason.", Guid.NewGuid(), ExpectedEndDate.AddDays(-2), Now));
    }

    [Fact]
    public void Extend_With_NewEndDate_One_Day_After_Current_ExpectedEndDate_Succeeds()
    {
        var record = CreateActiveRecord();

        record.Extend(ExpectedEndDate.AddDays(1), "Reason.", Guid.NewGuid(), ExpectedEndDate.AddDays(-1), Now);

        Assert.Equal(ExpectedEndDate.AddDays(1), record.ExpectedEndDate);
    }

    [Fact]
    public void Extend_With_NewEndDate_Equal_To_DecisionDate_Throws()
    {
        var record = CreateActiveRecord();
        var decisionDate = ExpectedEndDate.AddMonths(1);

        Assert.Throws<InvalidOperationException>(() =>
            record.Extend(decisionDate, "Reason.", Guid.NewGuid(), decisionDate, Now));
    }

    [Fact]
    public void Extend_With_NewEndDate_Before_DecisionDate_Throws()
    {
        var record = CreateActiveRecord();
        var decisionDate = ExpectedEndDate.AddMonths(1);

        Assert.Throws<InvalidOperationException>(() =>
            record.Extend(decisionDate.AddDays(-1), "Reason.", Guid.NewGuid(), decisionDate, Now));
    }

    [Fact]
    public void Extend_With_NewEndDate_One_Day_After_DecisionDate_Succeeds()
    {
        var record = CreateActiveRecord();
        var decisionDate = ExpectedEndDate.AddMonths(1);

        record.Extend(decisionDate.AddDays(1), "Reason.", Guid.NewGuid(), decisionDate, Now);

        Assert.Equal(decisionDate.AddDays(1), record.ExpectedEndDate);
    }

    // -------- ApplyAdministrativeCorrection --------

    [Fact]
    public void ApplyAdministrativeCorrection_On_Active_Record_Updates_Manager_EndDate_And_Notes_Only()
    {
        var record = CreateActiveRecord();
        var newManagerId = Guid.NewGuid();
        var newEndDate = ExpectedEndDate.AddDays(5);

        record.ApplyAdministrativeCorrection(newManagerId, newEndDate, "Corrected.", Now);

        Assert.Equal(newManagerId, record.ManagerEmployeeId);
        Assert.Equal(newEndDate, record.ExpectedEndDate);
        Assert.Equal("Corrected.", record.Notes);
        Assert.Equal(ProbationStatus.Active, record.Status);
        Assert.Null(record.ExtensionReason);
        Assert.Null(record.DecisionMakerEmployeeId);
        Assert.Null(record.DecisionDate);
        Assert.Null(record.OutcomeNotes);
    }

    [Fact]
    public void ApplyAdministrativeCorrection_On_ReviewDue_Record_Succeeds_And_Does_Not_Change_Status()
    {
        var record = CreateActiveRecord();
        record.MarkReviewDue(Now);

        record.ApplyAdministrativeCorrection(record.ManagerEmployeeId, ExpectedEndDate.AddDays(3), "Corrected.", Now);

        Assert.Equal(ProbationStatus.ReviewDue, record.Status);
        Assert.Equal(ExpectedEndDate.AddDays(3), record.ExpectedEndDate);
    }

    [Fact]
    public void ApplyAdministrativeCorrection_On_Extended_Record_Succeeds_And_Preserves_Outcome_Fields()
    {
        var record = CreateActiveRecord();
        var extendedEnd = ExpectedEndDate.AddMonths(1);
        var decisionMaker = Guid.NewGuid();
        record.Extend(extendedEnd, "Original reason.", decisionMaker, ExpectedEndDate.AddDays(-1), Now);

        record.ApplyAdministrativeCorrection(record.ManagerEmployeeId, extendedEnd.AddDays(2), "Corrected notes.", Now);

        Assert.Equal(ProbationStatus.Extended, record.Status);
        Assert.Equal(extendedEnd.AddDays(2), record.ExpectedEndDate);
        Assert.Equal("Corrected notes.", record.Notes);
        // Outcome fields set by the earlier Extend() must remain untouched by the correction.
        Assert.Equal("Original reason.", record.ExtensionReason);
        Assert.Equal(decisionMaker, record.DecisionMakerEmployeeId);
    }

    [Fact]
    public void ApplyAdministrativeCorrection_On_Passed_Record_Throws()
    {
        var record = CreateActiveRecord();
        record.Pass(Guid.NewGuid(), ExpectedEndDate, null, Now);

        Assert.Throws<InvalidOperationException>(() =>
            record.ApplyAdministrativeCorrection(Guid.NewGuid(), ExpectedEndDate.AddDays(1), "Attempted edit.", Now));
    }

    [Fact]
    public void ApplyAdministrativeCorrection_On_Failed_Record_Throws()
    {
        var record = CreateActiveRecord();
        record.Fail(Guid.NewGuid(), ExpectedEndDate, null, Now);

        Assert.Throws<InvalidOperationException>(() =>
            record.ApplyAdministrativeCorrection(Guid.NewGuid(), ExpectedEndDate.AddDays(1), "Attempted edit.", Now));
    }
}
