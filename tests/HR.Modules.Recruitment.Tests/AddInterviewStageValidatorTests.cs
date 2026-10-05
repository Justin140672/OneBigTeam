using HR.Modules.Recruitment.Features.AddInterviewStage;

namespace HR.Modules.Recruitment.Tests;

public class AddInterviewStageValidatorTests
{
    private readonly AddInterviewStageValidator _validator = new();

    [Fact]
    public void Valid_Without_Name() =>
        Assert.True(_validator.Validate(new AddInterviewStageRequest(Guid.NewGuid())).IsValid);

    [Fact]
    public void Valid_With_Name() =>
        Assert.True(_validator.Validate(new AddInterviewStageRequest(Guid.NewGuid(), "Panel")).IsValid);

    [Fact]
    public void Empty_CompanyId_Is_Invalid() =>
        Assert.False(_validator.Validate(new AddInterviewStageRequest(Guid.Empty)).IsValid);

    [Fact]
    public void Whitespace_Name_Is_Invalid() =>
        Assert.False(_validator.Validate(new AddInterviewStageRequest(Guid.NewGuid(), "   ")).IsValid);

    [Fact]
    public void Name_Over_100_Characters_Is_Invalid() =>
        Assert.False(_validator.Validate(new AddInterviewStageRequest(Guid.NewGuid(), new string('a', 101))).IsValid);
}
