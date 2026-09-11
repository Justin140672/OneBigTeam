using HR.Modules.Recruitment.Features.RespondToOffer;

namespace HR.Modules.Recruitment.Tests;

public class RespondToOfferValidatorTests
{
    private readonly RespondToOfferValidator _validator = new();

    private static RespondToOfferRequest Valid() => new()
    {
        CompanyId     = Guid.NewGuid(),
        VacancyId     = Guid.NewGuid(),
        ApplicationId = Guid.NewGuid(),
        Status        = "Accepted",
    };

    [Theory]
    [InlineData("Accepted")]
    [InlineData("declined")]
    [InlineData("WITHDRAWN")]
    public void Validate_Passes_For_Allowed_Target_Status_Case_Insensitive(string status)
    {
        Assert.True(_validator.Validate(Valid() with { Status = status }).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AwaitingResponse")]
    [InlineData("Rubbish")]
    public void Validate_Fails_For_Empty_Or_Disallowed_Status(string status)
    {
        var result = _validator.Validate(Valid() with { Status = status });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RespondToOfferRequest.Status));
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        Assert.False(_validator.Validate(Valid() with { CompanyId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_VacancyId_Is_Empty()
    {
        Assert.False(_validator.Validate(Valid() with { VacancyId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_ApplicationId_Is_Empty()
    {
        var result = _validator.Validate(Valid() with { ApplicationId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RespondToOfferRequest.ApplicationId));
    }
}
