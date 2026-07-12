using HR.Modules.Documents.Features.GetEmployeeProfilePhoto;

namespace HR.Modules.Documents.Tests;

public class GetEmployeeProfilePhotoValidatorTests
{
    private static readonly GetEmployeeProfilePhotoValidator Validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(new GetEmployeeProfilePhotoRequest(Guid.NewGuid(), Guid.NewGuid()));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(new GetEmployeeProfilePhotoRequest(Guid.Empty, Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeProfilePhotoRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyEmployeeId_Fails()
    {
        var result = Validator.Validate(new GetEmployeeProfilePhotoRequest(Guid.NewGuid(), Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeProfilePhotoRequest.EmployeeId));
    }

    [Fact]
    public void Validate_EmptyCompanyId_And_EmployeeId_Reports_Both_Errors()
    {
        var result = Validator.Validate(new GetEmployeeProfilePhotoRequest(Guid.Empty, Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeProfilePhotoRequest.CompanyId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeProfilePhotoRequest.EmployeeId));
    }
}
