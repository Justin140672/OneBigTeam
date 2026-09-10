using HR.Modules.Recruitment.Features.SaveCvReviewNotes;

namespace HR.Modules.Recruitment.Tests;

public class SaveCvReviewNotesValidatorTests
{
    private readonly SaveCvReviewNotesValidator _validator = new();

    private static SaveCvReviewNotesRequest Valid(string? notes = null) => new()
    {
        CompanyId     = Guid.NewGuid(),
        VacancyId     = Guid.NewGuid(),
        ApplicationId = Guid.NewGuid(),
        CvReviewNotes = notes,
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request() =>
        Assert.True(_validator.Validate(Valid("Looks good")).IsValid);

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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SaveCvReviewNotesRequest.CvReviewNotes));
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Empty()
    {
        var request = Valid() with { CompanyId = Guid.Empty };
        Assert.False(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_VacancyId_Empty()
    {
        var request = Valid() with { VacancyId = Guid.Empty };
        Assert.False(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_ApplicationId_Empty()
    {
        var request = Valid() with { ApplicationId = Guid.Empty };
        Assert.False(_validator.Validate(request).IsValid);
    }
}
