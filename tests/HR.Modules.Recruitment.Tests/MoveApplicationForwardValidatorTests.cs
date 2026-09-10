using HR.Modules.Recruitment.Features.MoveApplicationForward;

namespace HR.Modules.Recruitment.Tests;

public class MoveApplicationForwardValidatorTests
{
    private readonly MoveApplicationForwardValidator _validator = new();

    private static MoveApplicationForwardRequest Valid(string? notes = null) => new()
    {
        CompanyId     = Guid.NewGuid(),
        VacancyId     = Guid.NewGuid(),
        ApplicationId = Guid.NewGuid(),
        CvReviewNotes = notes,
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request() =>
        Assert.True(_validator.Validate(Valid("Advancing")).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Passes_When_Notes_Null_Empty_Or_Whitespace(string? notes) =>
        Assert.True(_validator.Validate(Valid(notes)).IsValid);

    [Fact]
    public void Validate_Passes_When_Notes_Exactly_4000_Chars() =>
        Assert.True(_validator.Validate(Valid(new string('A', 4000))).IsValid);

    [Fact]
    public void Validate_Fails_When_Notes_Exceeds_4000_Chars()
    {
        var result = _validator.Validate(Valid(new string('A', 4001)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(MoveApplicationForwardRequest.CvReviewNotes));
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Empty() =>
        Assert.False(_validator.Validate(Valid() with { CompanyId = Guid.Empty }).IsValid);

    [Fact]
    public void Validate_Fails_When_VacancyId_Empty() =>
        Assert.False(_validator.Validate(Valid() with { VacancyId = Guid.Empty }).IsValid);

    [Fact]
    public void Validate_Fails_When_ApplicationId_Empty() =>
        Assert.False(_validator.Validate(Valid() with { ApplicationId = Guid.Empty }).IsValid);
}
