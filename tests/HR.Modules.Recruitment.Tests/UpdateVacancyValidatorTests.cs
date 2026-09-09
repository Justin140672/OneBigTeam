using HR.Modules.Recruitment.Features.UpdateVacancy;

namespace HR.Modules.Recruitment.Tests;

public class UpdateVacancyValidatorTests
{
    private readonly UpdateVacancyValidator _validator = new();

    private static UpdateVacancyRequest Valid() => new()
    {
        CompanyId       = Guid.NewGuid(),
        VacancyId       = Guid.NewGuid(),
        AdvertTitle     = "Senior Software Engineer",
        HiringManagerId = Guid.NewGuid(),
        ExpectedVersion = 1,
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_VacancyId_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { VacancyId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateVacancyRequest.VacancyId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Passes_When_AdvertTitle_Is_Empty_Null_Or_Whitespace(string? advertTitle)
    {
        var result = _validator.Validate(Valid() with { AdvertTitle = advertTitle });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_AdvertTitle_Exceeds_Max_Length()
    {
        var result = _validator.Validate(Valid() with { AdvertTitle = new string('A', 201) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateVacancyRequest.AdvertTitle));
    }

    [Fact]
    public void Validate_Passes_When_PositionProfileId_Is_Null()
    {
        var result = _validator.Validate(Valid() with { PositionProfileId = null });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_PositionProfileId_Is_A_Valid_Guid()
    {
        var result = _validator.Validate(Valid() with { PositionProfileId = Guid.NewGuid() });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_PositionProfileId_Is_Guid_Empty()
    {
        var result = _validator.Validate(Valid() with { PositionProfileId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateVacancyRequest.PositionProfileId));
    }

    [Fact]
    public void Validate_Fails_When_AdvertDescription_Exceeds_Max_Length()
    {
        var result = _validator.Validate(Valid() with { AdvertDescription = new string('A', 4001) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateVacancyRequest.AdvertDescription));
    }

    [Fact]
    public void Validate_Fails_When_IsAuthorisedCorrection_True_And_CorrectionReason_Is_Null()
    {
        var result = _validator.Validate(Valid() with { IsAuthorisedCorrection = true, CorrectionReason = null });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateVacancyRequest.CorrectionReason));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Fails_When_IsAuthorisedCorrection_True_And_CorrectionReason_Is_Empty_Or_Whitespace(string correctionReason)
    {
        var result = _validator.Validate(Valid() with { IsAuthorisedCorrection = true, CorrectionReason = correctionReason });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateVacancyRequest.CorrectionReason));
    }

    [Fact]
    public void Validate_Passes_When_IsAuthorisedCorrection_True_And_CorrectionReason_Is_NonEmpty()
    {
        var result = _validator.Validate(Valid() with
        {
            IsAuthorisedCorrection = true,
            CorrectionReason = "Vacancy created against the wrong position profile.",
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_IsAuthorisedCorrection_False_And_CorrectionReason_Is_Null()
    {
        var result = _validator.Validate(Valid() with { IsAuthorisedCorrection = false, CorrectionReason = null });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_IsAuthorisedCorrection_True_And_CorrectionReason_Exceeds_Max_Length()
    {
        var result = _validator.Validate(Valid() with { IsAuthorisedCorrection = true, CorrectionReason = new string('A', 1001) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateVacancyRequest.CorrectionReason));
    }

    [Fact]
    public void Validate_Fails_When_ExpectedVersion_Is_Null()
    {
        var result = _validator.Validate(Valid() with { ExpectedVersion = null });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateVacancyRequest.ExpectedVersion));
    }
}
