using HR.Modules.Documents.Features.GetPendingProfilePhoto;

namespace HR.Modules.Documents.Tests;

public class GetPendingProfilePhotoValidatorTests
{
    private static readonly GetPendingProfilePhotoValidator Validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(new GetPendingProfilePhotoRequest(Guid.NewGuid(), Guid.NewGuid()));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(new GetPendingProfilePhotoRequest(Guid.Empty, Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetPendingProfilePhotoRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyEmployeeId_Fails()
    {
        var result = Validator.Validate(new GetPendingProfilePhotoRequest(Guid.NewGuid(), Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetPendingProfilePhotoRequest.EmployeeId));
    }

    [Fact]
    public void Validate_EmptyCompanyId_And_EmployeeId_Reports_Both_Errors()
    {
        var result = Validator.Validate(new GetPendingProfilePhotoRequest(Guid.Empty, Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetPendingProfilePhotoRequest.CompanyId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetPendingProfilePhotoRequest.EmployeeId));
    }
}
