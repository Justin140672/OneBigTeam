using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

public class InterviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Interview CreateInterview() =>
        Interview.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(3), 30, "Remote", Now);

    [Fact]
    public void Create_Sets_Outcome_To_Pending()
    {
        var interview = CreateInterview();

        Assert.Equal(InterviewOutcome.Pending, interview.Outcome);
        Assert.Null(interview.Notes);
    }

    [Fact]
    public void Create_Trims_Location()
    {
        var interview = Interview.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(3), 30, "  Remote  ", Now);

        Assert.Equal("Remote", interview.Location);
    }

    [Fact]
    public void Reschedule_Updates_ScheduledAt_Duration_And_Location()
    {
        var interview = CreateInterview();
        var newTime = Now.AddDays(5);

        interview.Reschedule(newTime, 45, "Office - Room 2", Now);

        Assert.Equal(newTime, interview.ScheduledAt);
        Assert.Equal(45, interview.DurationMinutes);
        Assert.Equal("Office - Room 2", interview.Location);
    }

    [Fact]
    public void Reschedule_After_Outcome_Recorded_Throws()
    {
        var interview = CreateInterview();
        interview.RecordOutcome(InterviewOutcome.Passed, null, Now);

        Assert.Throws<InvalidOperationException>(() => interview.Reschedule(Now.AddDays(1), null, null, Now));
    }

    [Fact]
    public void RecordOutcome_Sets_Outcome_And_Notes()
    {
        var interview = CreateInterview();

        interview.RecordOutcome(InterviewOutcome.Passed, "Strong technical skills.", Now);

        Assert.Equal(InterviewOutcome.Passed, interview.Outcome);
        Assert.Equal("Strong technical skills.", interview.Notes);
    }

    [Fact]
    public void RecordOutcome_Twice_Throws()
    {
        var interview = CreateInterview();
        interview.RecordOutcome(InterviewOutcome.Failed, null, Now);

        Assert.Throws<InvalidOperationException>(() => interview.RecordOutcome(InterviewOutcome.Passed, null, Now));
    }

    [Fact]
    public void RecordOutcome_Of_Pending_Throws()
    {
        var interview = CreateInterview();

        Assert.Throws<InvalidOperationException>(() => interview.RecordOutcome(InterviewOutcome.Pending, null, Now));
    }

    [Fact]
    public void Cancel_Sets_Outcome_To_Cancelled()
    {
        var interview = CreateInterview();

        interview.Cancel(Now);

        Assert.Equal(InterviewOutcome.Cancelled, interview.Outcome);
    }

    [Fact]
    public void Cancel_After_Outcome_Recorded_Throws()
    {
        var interview = CreateInterview();
        interview.RecordOutcome(InterviewOutcome.NoShow, null, Now);

        Assert.Throws<InvalidOperationException>(() => interview.Cancel(Now));
    }
}
