using HR.Modules.Recruitment.Features.SetApplicationCv;
using HR.SharedKernel;

namespace HR.Modules.Recruitment.Tests;

public class SetApplicationCvValidatorTests
{
    private readonly SetApplicationCvValidator _validator = new();

    private static SetApplicationCvRequest Valid(Guid? cvDocumentId = null) => new()
    {
        CompanyId       = Guid.NewGuid(),
        VacancyId       = Guid.NewGuid(),
        ApplicationId   = Guid.NewGuid(),
        CvDocumentId    = cvDocumentId ?? Guid.NewGuid(),
        ExpectedVersion = 1,
    };

    [Fact]
    public void Validate_Passes_For_Valid_Attach_Request() =>
        Assert.True(_validator.Validate(Valid()).IsValid);

    [Fact]
    public void Validate_Passes_When_CvDocumentId_Is_Null_To_Remove_The_Reference()
    {
        var request = Valid() with { CvDocumentId = null };

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CvDocumentId_Is_Empty_Guid()
    {
        var result = _validator.Validate(Valid() with { CvDocumentId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetApplicationCvRequest.CvDocumentId));
    }

    [Fact]
    public void Validate_Fails_When_ExpectedVersion_Missing()
    {
        var result = _validator.Validate(Valid() with { ExpectedVersion = null });

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == nameof(SetApplicationCvRequest.ExpectedVersion));
        Assert.Equal(ConcurrencyValidationExtensions.MissingVersionMessage, error.ErrorMessage);
    }

    [Fact]
    public void Validate_Fails_When_ExpectedVersion_Missing_Even_For_A_Remove_Request()
    {
        var result = _validator.Validate(Valid() with { CvDocumentId = null, ExpectedVersion = null });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetApplicationCvRequest.ExpectedVersion));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    public void Validate_Passes_For_Any_Supplied_ExpectedVersion(int expectedVersion)
    {
        // RequireLoadedVersion only requires presence; staleness is the handler's job (409).
        Assert.True(_validator.Validate(Valid() with { ExpectedVersion = expectedVersion }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Empty()
    {
        var result = _validator.Validate(Valid() with { CompanyId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetApplicationCvRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_VacancyId_Empty()
    {
        var result = _validator.Validate(Valid() with { VacancyId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetApplicationCvRequest.VacancyId));
    }

    [Fact]
    public void Validate_Fails_When_ApplicationId_Empty()
    {
        var result = _validator.Validate(Valid() with { ApplicationId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetApplicationCvRequest.ApplicationId));
    }
}
