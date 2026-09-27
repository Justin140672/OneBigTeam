using HR.Modules.Recruitment.Features.ApplyForInternalVacancy;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 4: request validation for an employee's self-application to an internal
/// vacancy. File size/type rules are enforced by the handler (see ApplyForInternalVacancyHandlerTests).
/// </summary>
public class ApplyForInternalVacancyValidatorTests
{
    private readonly ApplyForInternalVacancyValidator _validator = new();

    private static IFormFile FakePdf() =>
        new FormFile(new MemoryStream(new byte[128]), 0, 128, "CvFile", "cv.pdf")
        {
            Headers     = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

    private static ApplyForInternalVacancyRequest Valid(
        Guid? companyId = null,
        Guid? vacancyId = null,
        bool withCv = true) =>
        new()
        {
            CompanyId = companyId ?? Guid.NewGuid(),
            VacancyId = vacancyId ?? Guid.NewGuid(),
            CvFile    = withCv ? FakePdf() : null,
        };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(Valid());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(Valid(companyId: Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApplyForInternalVacancyRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_VacancyId_Is_Empty()
    {
        var result = _validator.Validate(Valid(vacancyId: Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApplyForInternalVacancyRequest.VacancyId));
    }

    [Fact]
    public void Validate_Fails_When_CvFile_Is_Missing()
    {
        var result = _validator.Validate(Valid(withCv: false));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == nameof(ApplyForInternalVacancyRequest.CvFile));
        Assert.Equal("A CV file is required to apply.", error.ErrorMessage);
    }

    [Fact]
    public void Validate_Reports_Every_Failing_Field_Together()
    {
        var result = _validator.Validate(new ApplyForInternalVacancyRequest());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApplyForInternalVacancyRequest.CompanyId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApplyForInternalVacancyRequest.VacancyId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApplyForInternalVacancyRequest.CvFile));
    }
}
