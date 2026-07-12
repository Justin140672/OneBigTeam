using HR.Modules.Documents.Features.ApproveProfilePhoto;

namespace HR.Modules.Documents.Tests;

public class ApproveProfilePhotoValidatorTests
{
    private static readonly ApproveProfilePhotoValidator Validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(new ApproveProfilePhotoRequest(Guid.NewGuid(), Guid.NewGuid()));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(new ApproveProfilePhotoRequest(Guid.Empty, Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApproveProfilePhotoRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyEmployeeId_Fails()
    {
        var result = Validator.Validate(new ApproveProfilePhotoRequest(Guid.NewGuid(), Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApproveProfilePhotoRequest.EmployeeId));
    }

    [Fact]
    public void Validate_EmptyCompanyId_And_EmployeeId_Reports_Both_Errors()
    {
        var result = Validator.Validate(new ApproveProfilePhotoRequest(Guid.Empty, Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApproveProfilePhotoRequest.CompanyId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApproveProfilePhotoRequest.EmployeeId));
    }
}
