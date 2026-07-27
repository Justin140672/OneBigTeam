using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

public class ApplicationStatusTransitionsTests
{
    [Theory]
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.Screening)]
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.InterviewScheduled)]
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.Withdrawn)]
    [InlineData(ApplicationStatus.Screening, ApplicationStatus.InterviewScheduled)]
    [InlineData(ApplicationStatus.Screening, ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Screening, ApplicationStatus.Withdrawn)]
    [InlineData(ApplicationStatus.InterviewScheduled, ApplicationStatus.Interviewed)]
    [InlineData(ApplicationStatus.InterviewScheduled, ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.InterviewScheduled, ApplicationStatus.Withdrawn)]
    [InlineData(ApplicationStatus.Interviewed, ApplicationStatus.Offered)]
    [InlineData(ApplicationStatus.Interviewed, ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Interviewed, ApplicationStatus.Withdrawn)]
    [InlineData(ApplicationStatus.Offered, ApplicationStatus.Hired)]
    [InlineData(ApplicationStatus.Offered, ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Offered, ApplicationStatus.Withdrawn)]
    internal void CanTransitionTo_Returns_True_For_Every_Allowed_Transition(ApplicationStatus from, ApplicationStatus to)
    {
        Assert.True(ApplicationStatusTransitions.CanTransitionTo(from, to));
    }

    [Theory]
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.Interviewed)]
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.Offered)]
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.Hired)]
    [InlineData(ApplicationStatus.Applied, ApplicationStatus.Applied)]
    [InlineData(ApplicationStatus.Screening, ApplicationStatus.Applied)]
    [InlineData(ApplicationStatus.Screening, ApplicationStatus.Offered)]
    [InlineData(ApplicationStatus.Screening, ApplicationStatus.Hired)]
    [InlineData(ApplicationStatus.InterviewScheduled, ApplicationStatus.Applied)]
    [InlineData(ApplicationStatus.InterviewScheduled, ApplicationStatus.Screening)]
    [InlineData(ApplicationStatus.InterviewScheduled, ApplicationStatus.Offered)]
    [InlineData(ApplicationStatus.InterviewScheduled, ApplicationStatus.Hired)]
    [InlineData(ApplicationStatus.Interviewed, ApplicationStatus.Applied)]
    [InlineData(ApplicationStatus.Interviewed, ApplicationStatus.Screening)]
    [InlineData(ApplicationStatus.Interviewed, ApplicationStatus.InterviewScheduled)]
    [InlineData(ApplicationStatus.Interviewed, ApplicationStatus.Hired)]
    [InlineData(ApplicationStatus.Offered, ApplicationStatus.Applied)]
    [InlineData(ApplicationStatus.Offered, ApplicationStatus.Interviewed)]
    [InlineData(ApplicationStatus.Hired, ApplicationStatus.Applied)]
    [InlineData(ApplicationStatus.Hired, ApplicationStatus.Screening)]
    [InlineData(ApplicationStatus.Hired, ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Hired, ApplicationStatus.Withdrawn)]
    [InlineData(ApplicationStatus.Rejected, ApplicationStatus.Applied)]
    [InlineData(ApplicationStatus.Rejected, ApplicationStatus.Hired)]
    [InlineData(ApplicationStatus.Rejected, ApplicationStatus.Withdrawn)]
    [InlineData(ApplicationStatus.Withdrawn, ApplicationStatus.Applied)]
    [InlineData(ApplicationStatus.Withdrawn, ApplicationStatus.Hired)]
    [InlineData(ApplicationStatus.Withdrawn, ApplicationStatus.Rejected)]
    internal void CanTransitionTo_Returns_False_For_Invalid_Transitions(ApplicationStatus from, ApplicationStatus to)
    {
        Assert.False(ApplicationStatusTransitions.CanTransitionTo(from, to));
    }

    [Fact]
    public void GetAllowedNextStages_Returns_Expected_Set_For_Applied()
    {
        var allowed = ApplicationStatusTransitions.GetAllowedNextStages(ApplicationStatus.Applied);

        Assert.Equal(
            new[] { ApplicationStatus.Screening, ApplicationStatus.InterviewScheduled, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn },
            allowed);
    }

    [Theory]
    [InlineData(ApplicationStatus.Hired)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Withdrawn)]
    internal void GetAllowedNextStages_Returns_Empty_For_Terminal_Stages(ApplicationStatus terminalStage)
    {
        var allowed = ApplicationStatusTransitions.GetAllowedNextStages(terminalStage);

        Assert.Empty(allowed);
    }
}
