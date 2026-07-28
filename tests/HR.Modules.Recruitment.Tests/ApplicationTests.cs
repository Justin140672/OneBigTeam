using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

public class ApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Application CreateApplication(Guid? initialStageId = null) =>
        Application.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), initialStageId ?? Guid.NewGuid(), null, Now);

    [Fact]
    public void Create_Sets_CurrentStageId_To_InitialStageId()
    {
        var stageId = Guid.NewGuid();
        var application = CreateApplication(stageId);

        Assert.Equal(stageId, application.CurrentStageId);
        Assert.Null(application.InterviewOutcome);
        Assert.Null(application.WithdrawnAt);
    }

    [Fact]
    public void SetInterviewOutcome_Sets_Outcome_And_UpdatedAt_Without_Changing_Stage()
    {
        var stageId = Guid.NewGuid();
        var application = CreateApplication(stageId);
        var later = Now.AddDays(1);

        application.SetInterviewOutcome(InterviewOutcome.Pending, later);

        Assert.Equal(InterviewOutcome.Pending, application.InterviewOutcome);
        Assert.Equal(stageId, application.CurrentStageId);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void MoveToStage_Updates_CurrentStageId_And_UpdatedAt()
    {
        var application = CreateApplication();
        var newStageId = Guid.NewGuid();
        var later = Now.AddDays(1);

        application.MoveToStage(newStageId, later);

        Assert.Equal(newStageId, application.CurrentStageId);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void RecordRejection_Sets_Stage_And_RejectionReason()
    {
        var application = CreateApplication();
        var rejectedStageId = Guid.NewGuid();
        var later = Now.AddDays(1);

        application.RecordRejection(rejectedStageId, "Not enough experience.", later);

        Assert.Equal(rejectedStageId, application.CurrentStageId);
        Assert.Equal("Not enough experience.", application.RejectionReason);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void RecordRejection_Trims_Whitespace_Only_Reason_To_Null()
    {
        var application = CreateApplication();

        application.RecordRejection(Guid.NewGuid(), "   ", Now.AddDays(1));

        Assert.Null(application.RejectionReason);
    }

    [Fact]
    public void RecordHire_Sets_Stage_And_UpdatedAt()
    {
        var application = CreateApplication();
        var hiredStageId = Guid.NewGuid();
        var later = Now.AddDays(1);

        application.RecordHire(hiredStageId, later);

        Assert.Equal(hiredStageId, application.CurrentStageId);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void Withdraw_Sets_WithdrawnAt_But_Does_Not_Change_CurrentStageId()
    {
        var stageId = Guid.NewGuid();
        var application = CreateApplication(stageId);
        var later = Now.AddDays(1);

        application.Withdraw(later);

        Assert.Equal(later, application.WithdrawnAt);
        Assert.Equal(stageId, application.CurrentStageId);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void Create_With_Source_ExternalRecruiter_Sets_SourceExternalRecruiterId()
    {
        var recruiterId = Guid.NewGuid();

        var application = Application.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now,
            ApplicationSource.ExternalRecruiter, recruiterId);

        Assert.Equal(ApplicationSource.ExternalRecruiter, application.Source);
        Assert.Equal(recruiterId, application.SourceExternalRecruiterId);
    }

    [Fact]
    public void Create_Forces_SourceExternalRecruiterId_Null_When_Source_Is_Not_ExternalRecruiter()
    {
        var suppliedRecruiterId = Guid.NewGuid();

        var application = Application.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now,
            ApplicationSource.Direct, suppliedRecruiterId);

        Assert.Equal(ApplicationSource.Direct, application.Source);
        Assert.Null(application.SourceExternalRecruiterId);
    }

    [Fact]
    public void Create_Without_Source_Leaves_Source_And_RecruiterId_Null()
    {
        var application = CreateApplication();

        Assert.Null(application.Source);
        Assert.Null(application.SourceExternalRecruiterId);
    }

    [Fact]
    public void SetSource_ExternalRecruiter_Sets_SourceExternalRecruiterId_And_UpdatedAt()
    {
        var application = CreateApplication();
        var recruiterId = Guid.NewGuid();
        var later = Now.AddDays(1);

        application.SetSource(ApplicationSource.ExternalRecruiter, recruiterId, later);

        Assert.Equal(ApplicationSource.ExternalRecruiter, application.Source);
        Assert.Equal(recruiterId, application.SourceExternalRecruiterId);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void SetSource_Forces_SourceExternalRecruiterId_Null_When_Source_Not_ExternalRecruiter_Even_If_Id_Supplied()
    {
        var application = CreateApplication();
        application.SetSource(ApplicationSource.ExternalRecruiter, Guid.NewGuid(), Now);
        var suppliedRecruiterId = Guid.NewGuid();

        application.SetSource(ApplicationSource.Direct, suppliedRecruiterId, Now.AddDays(1));

        Assert.Equal(ApplicationSource.Direct, application.Source);
        Assert.Null(application.SourceExternalRecruiterId);
    }

    [Fact]
    public void SetSource_Null_Clears_Source_And_RecruiterId()
    {
        var application = CreateApplication();
        application.SetSource(ApplicationSource.ExternalRecruiter, Guid.NewGuid(), Now);

        application.SetSource(null, null, Now.AddDays(1));

        Assert.Null(application.Source);
        Assert.Null(application.SourceExternalRecruiterId);
    }
}
