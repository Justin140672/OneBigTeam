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

    [Theory]
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.Screening)]
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.InterviewScheduled)]
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.Withdrawn)]
    [InlineData(ApplicationStatus.Screening, ApplicationStatus.InterviewScheduled)]
    [InlineData(ApplicationStatus.InterviewScheduled, ApplicationStatus.Interviewed)]
    [InlineData(ApplicationStatus.Interviewed, ApplicationStatus.Offered)]
    [InlineData(ApplicationStatus.Offered, ApplicationStatus.Hired)]
    internal void MoveToStage_Valid_Transition_Updates_Status_And_UpdatedAt(ApplicationStatus from, ApplicationStatus to)
    {
        var application = CreateApplication();
        MoveApplicationToStatus(application, from);
        var later = Now.AddDays(1);

        application.MoveToStage(to, later);

        Assert.Equal(to, application.Status);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void MoveToStage_Invalid_Transition_Throws_With_Message_Naming_Both_Stages()
    {
        var application = CreateApplication();
        application.MoveToScreening(Now);
        application.ScheduleInterview(Now);
        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);

        var ex = Assert.Throws<InvalidOperationException>(
            () => application.MoveToStage(ApplicationStatus.Applied, Now.AddDays(1)));

        Assert.Contains("Interviewed", ex.Message);
        Assert.Contains("Applied", ex.Message);
    }

    [Fact]
    public void MoveToStage_Invalid_Transition_Does_Not_Change_Status_Or_UpdatedAt()
    {
        var application = CreateApplication();
        var originalUpdatedAt = application.UpdatedAt;

        Assert.Throws<InvalidOperationException>(
            () => application.MoveToStage(ApplicationStatus.Hired, Now.AddDays(1)));

        Assert.Equal(ApplicationStatus.Applied, application.Status);
        Assert.Equal(originalUpdatedAt, application.UpdatedAt);
    }

    [Fact]
    public void MoveToStage_From_Terminal_Stage_Throws()
    {
        var application = CreateApplication();
        application.Reject(Now);

        Assert.Throws<InvalidOperationException>(
            () => application.MoveToStage(ApplicationStatus.Screening, Now.AddDays(1)));
    }

    private static void MoveApplicationToStatus(Application application, ApplicationStatus target)
    {
        if (target == ApplicationStatus.Applied)
            return;

        application.MoveToScreening(Now);
        if (target == ApplicationStatus.Screening)
            return;

        application.ScheduleInterview(Now);
        if (target == ApplicationStatus.InterviewScheduled)
            return;

        application.RecordInterviewOutcome(InterviewOutcome.Passed, Now);
        if (target == ApplicationStatus.Interviewed)
            return;

        application.Offer(Now);
        if (target == ApplicationStatus.Offered)
            return;

        application.Hire(Now);
    }
}
