using HR.Modules.Recruitment.Features.CreateVacancy;

namespace HR.Modules.Recruitment.Tests;

public class CreateVacancyValidatorTests
{
    private readonly CreateVacancyValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new CreateVacancyRequest
        {
            CompanyId         = Guid.NewGuid(),
            PositionProfileId = Guid.NewGuid(),
            AdvertTitle       = "Senior Software Engineer",
            HiringManagerId   = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_PositionProfileId_Is_Empty()
    {
        var result = _validator.Validate(new CreateVacancyRequest
        {
            CompanyId         = Guid.NewGuid(),
            PositionProfileId = Guid.Empty,
            AdvertTitle       = "Senior Software Engineer",
            HiringManagerId   = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateVacancyRequest.PositionProfileId));
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new CreateVacancyRequest
        {
            CompanyId       = Guid.Empty,
            AdvertTitle     = "Senior Software Engineer",
            HiringManagerId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateVacancyRequest.CompanyId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Passes_When_AdvertTitle_Is_Empty_Null_Or_Whitespace(string? advertTitle)
    {
        var result = _validator.Validate(new CreateVacancyRequest
        {
            CompanyId         = Guid.NewGuid(),
            PositionProfileId = Guid.NewGuid(),
            AdvertTitle       = advertTitle,
            HiringManagerId   = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_AdvertTitle_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new CreateVacancyRequest
        {
            CompanyId       = Guid.NewGuid(),
            AdvertTitle     = new string('A', 201),
            HiringManagerId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateVacancyRequest.AdvertTitle));
    }

    [Fact]
    public void Validate_Fails_When_HiringManagerId_Is_Empty()
    {
        var result = _validator.Validate(new CreateVacancyRequest
        {
            CompanyId       = Guid.NewGuid(),
            AdvertTitle     = "Senior Software Engineer",
            HiringManagerId = Guid.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateVacancyRequest.HiringManagerId));
    }

    [Fact]
    public void Validate_Fails_When_AdvertDescription_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new CreateVacancyRequest
        {
            CompanyId         = Guid.NewGuid(),
            AdvertTitle       = "Senior Software Engineer",
            AdvertDescription = new string('A', 4001),
            HiringManagerId   = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateVacancyRequest.AdvertDescription));
    }

    [Fact]
    public void Validate_Passes_When_AdvertDescription_Is_At_Max_Length()
    {
        var result = _validator.Validate(new CreateVacancyRequest
        {
            CompanyId         = Guid.NewGuid(),
            PositionProfileId = Guid.NewGuid(),
            AdvertTitle       = "Senior Software Engineer",
            AdvertDescription = new string('A', 4000),
            HiringManagerId   = Guid.NewGuid(),
        });

        Assert.True(result.IsValid);
    }
}
