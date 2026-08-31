using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UpdateRecruitmentStage;

namespace HR.Modules.Recruitment.Tests;

public class UpdateRecruitmentStageValidatorTests
{
    private readonly UpdateRecruitmentStageValidator _validator = new();

    private static UpdateRecruitmentStageRequest Valid(bool isTerminal = false, RecruitmentStagePurpose? purpose = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Interview", isTerminal,
            isTerminal ? RecruitmentStageTerminalOutcome.Hired : RecruitmentStageTerminalOutcome.None, purpose);

    [Fact]
    public void Passes_When_Purpose_Is_Null()
    {
        Assert.True(_validator.Validate(Valid(purpose: null)).IsValid);
    }

    [Fact]
    public void Passes_For_Every_Valid_Purpose_On_NonTerminal_Stage()
    {
        foreach (RecruitmentStagePurpose purpose in Enum.GetValues<RecruitmentStagePurpose>())
            Assert.True(_validator.Validate(Valid(purpose: purpose)).IsValid, $"purpose {purpose}");
    }

    [Fact]
    public void Fails_For_Undefined_Purpose_Enum_Value()
    {
        var result = _validator.Validate(Valid(purpose: (RecruitmentStagePurpose)999));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateRecruitmentStageRequest.Purpose));
    }

    [Fact]
    public void Fails_When_Purpose_Set_On_Terminal_Stage()
    {
        var result = _validator.Validate(Valid(isTerminal: true, purpose: RecruitmentStagePurpose.Interview));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateRecruitmentStageRequest.Purpose));
    }

    [Fact]
    public void Passes_When_Terminal_Stage_Has_Null_Purpose()
    {
        Assert.True(_validator.Validate(Valid(isTerminal: true, purpose: null)).IsValid);
    }
}
