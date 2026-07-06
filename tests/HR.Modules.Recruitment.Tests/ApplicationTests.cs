using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

public class ApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Application CreateApplication() =>
        Application.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now);

    [Fact]
    public void Create_Sets_Status_To_Applied()
    {
        var application = CreateApplication();

        Assert.Equal(ApplicationStatus.Applied, application.Status);
        Assert.Null(application.InterviewOutcome);
    }

    [Fact]
    public void Full_Happy_Path_Reaches_Hired_With_Passed_Outcome()
    {
        var application = CreateApplication();

        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        application.Offer(Now);
        application.Hire(Now);

        Assert.Equal(ApplicationStatus.Hired, application.Status);
        Assert.Equal(InterviewOutcome.Passed, application.InterviewOutcome);
    }

    [Fact]
    public void ScheduleInterview_Sets_Outcome_To_Pending()
    {
        var application = CreateApplication();

        application.ScheduleInterview(Now);

        Assert.Equal(ApplicationStatus.InterviewScheduled, application.Status);
        Assert.Equal(InterviewOutcome.Pending, application.InterviewOutcome);
    }

    [Fact]
    public void RecordInterviewOutcome_Before_Scheduled_Throws()
    {
        var application = CreateApplication();

        Assert.Throws<InvalidOperationException>(() => application.RecordInterviewOutcome(InterviewOutcome.Passed, Now));
    }

    [Fact]
    public void Offer_Before_Interviewed_Throws()
    {
        var application = CreateApplication();

        Assert.Throws<InvalidOperationException>(() => application.Offer(Now));
    }

    [Fact]
    public void Reject_From_Applied_Sets_Status_To_Rejected()
    {
        var application = CreateApplication();

        application.Reject(Now);

        Assert.Equal(ApplicationStatus.Rejected, application.Status);
    }

    [Fact]
    public void Reject_After_Hired_Throws()
    {
        var application = CreateApplication();
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        application.Offer(Now);
        application.Hire(Now);

        Assert.Throws<InvalidOperationException>(() => application.Reject(Now));
    }

    [Fact]
    public void Withdraw_From_Applied_Sets_Status_To_Withdrawn()
    {
        var application = CreateApplication();

        application.Withdraw(Now);

        Assert.Equal(ApplicationStatus.Withdrawn, application.Status);
    }
}
