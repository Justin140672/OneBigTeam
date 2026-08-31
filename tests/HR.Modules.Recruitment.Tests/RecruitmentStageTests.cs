using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

public class RecruitmentStageTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_Fields_And_Defaults_IsActive_True()
    {
        var companyId = Guid.NewGuid();

        var stage = RecruitmentStage.Create(Guid.NewGuid(), companyId, " Interview ", 3, false, RecruitmentStageTerminalOutcome.None, Now);

        Assert.Equal(companyId, stage.CompanyId);
        Assert.Equal("Interview", stage.Name);
        Assert.Equal(3, stage.DisplayOrder);
        Assert.True(stage.IsActive);
        Assert.False(stage.IsTerminal);
        Assert.Equal(RecruitmentStageTerminalOutcome.None, stage.TerminalOutcome);
        Assert.Equal(Now, stage.CreatedAt);
        Assert.Equal(Now, stage.UpdatedAt);
    }

    [Fact]
    public void Create_Forces_TerminalOutcome_None_When_Not_Terminal()
    {
        var stage = RecruitmentStage.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Offer", 4, false, RecruitmentStageTerminalOutcome.Hired, Now);

        Assert.False(stage.IsTerminal);
        Assert.Equal(RecruitmentStageTerminalOutcome.None, stage.TerminalOutcome);
    }

    [Fact]
    public void Create_Keeps_TerminalOutcome_When_Terminal()
    {
        var stage = RecruitmentStage.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Hired", 5, true, RecruitmentStageTerminalOutcome.Hired, Now);

        Assert.True(stage.IsTerminal);
        Assert.Equal(RecruitmentStageTerminalOutcome.Hired, stage.TerminalOutcome);
    }

    [Fact]
    public void UpdateDetails_Updates_Name_Terminal_Flag_And_UpdatedAt()
    {
        var stage = RecruitmentStage.Create(Guid.NewGuid(), Guid.NewGuid(), "Offer", 4, false, RecruitmentStageTerminalOutcome.None, Now);
        var later = Now.AddDays(1);

        stage.UpdateDetails(" Offer Extended ", true, RecruitmentStageTerminalOutcome.Hired, later);

        Assert.Equal("Offer Extended", stage.Name);
        Assert.True(stage.IsTerminal);
        Assert.Equal(RecruitmentStageTerminalOutcome.Hired, stage.TerminalOutcome);
        Assert.Equal(later, stage.UpdatedAt);
    }

    [Fact]
    public void UpdateDetails_Forces_TerminalOutcome_None_When_Not_Terminal()
    {
        var stage = RecruitmentStage.Create(Guid.NewGuid(), Guid.NewGuid(), "Hired", 5, true, RecruitmentStageTerminalOutcome.Hired, Now);

        stage.UpdateDetails("Hired", false, RecruitmentStageTerminalOutcome.Hired, Now.AddDays(1));

        Assert.False(stage.IsTerminal);
        Assert.Equal(RecruitmentStageTerminalOutcome.None, stage.TerminalOutcome);
    }

    [Fact]
    public void Create_Sets_Purpose_On_NonTerminal_Stage()
    {
        var stage = RecruitmentStage.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Application Received", 1, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.NewApplication);

        Assert.Equal(RecruitmentStagePurpose.NewApplication, stage.Purpose);
    }

    [Fact]
    public void Create_Defaults_Purpose_To_Null()
    {
        var stage = RecruitmentStage.Create(
            Guid.NewGuid(), Guid.NewGuid(), "CV Review", 2, false, RecruitmentStageTerminalOutcome.None, Now);

        Assert.Null(stage.Purpose);
    }

    [Fact]
    public void Create_Forces_Purpose_Null_On_Terminal_Stage()
    {
        var stage = RecruitmentStage.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Hired", 5, true, RecruitmentStageTerminalOutcome.Hired, Now, RecruitmentStagePurpose.Offer);

        Assert.Null(stage.Purpose);
    }

    [Fact]
    public void UpdateDetails_Sets_Purpose_On_NonTerminal_Stage()
    {
        var stage = RecruitmentStage.Create(Guid.NewGuid(), Guid.NewGuid(), "Offer", 4, false, RecruitmentStageTerminalOutcome.None, Now);

        stage.UpdateDetails("Offer", false, RecruitmentStageTerminalOutcome.None, Now.AddDays(1), RecruitmentStagePurpose.Offer);

        Assert.Equal(RecruitmentStagePurpose.Offer, stage.Purpose);
    }

    [Fact]
    public void UpdateDetails_Forces_Purpose_Null_When_Made_Terminal()
    {
        var stage = RecruitmentStage.Create(Guid.NewGuid(), Guid.NewGuid(), "Offer", 4, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Offer);

        stage.UpdateDetails("Offer", true, RecruitmentStageTerminalOutcome.Hired, Now.AddDays(1), RecruitmentStagePurpose.Offer);

        Assert.Null(stage.Purpose);
    }

    [Fact]
    public void UpdateDetails_Can_Clear_Purpose_By_Passing_Null()
    {
        var stage = RecruitmentStage.Create(Guid.NewGuid(), Guid.NewGuid(), "Offer", 4, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Offer);

        stage.UpdateDetails("Offer", false, RecruitmentStageTerminalOutcome.None, Now.AddDays(1));

        Assert.Null(stage.Purpose);
    }

    [Fact]
    public void SetDisplayOrder_Updates_DisplayOrder_And_UpdatedAt()
    {
        var stage = RecruitmentStage.Create(Guid.NewGuid(), Guid.NewGuid(), "Offer", 4, false, RecruitmentStageTerminalOutcome.None, Now);
        var later = Now.AddDays(1);

        stage.SetDisplayOrder(7, later);

        Assert.Equal(7, stage.DisplayOrder);
        Assert.Equal(later, stage.UpdatedAt);
    }

    [Fact]
    public void SetActiveStatus_Updates_IsActive_And_UpdatedAt()
    {
        var stage = RecruitmentStage.Create(Guid.NewGuid(), Guid.NewGuid(), "Offer", 4, false, RecruitmentStageTerminalOutcome.None, Now);
        var later = Now.AddDays(1);

        stage.SetActiveStatus(false, later);

        Assert.False(stage.IsActive);
        Assert.Equal(later, stage.UpdatedAt);
    }
}
