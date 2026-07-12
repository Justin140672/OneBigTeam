using HR.Modules.Documents.Features.RejectProfilePhoto;

namespace HR.Modules.Documents.Tests;

public class RejectProfilePhotoValidatorTests
{
    private static readonly RejectProfilePhotoValidator Validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(new RejectProfilePhotoRequest(Guid.NewGuid(), Guid.NewGuid(), "Blurry"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ValidRequest_With_Null_RejectionReason_Passes()
    {
        var result = Validator.Validate(new RejectProfilePhotoRequest(Guid.NewGuid(), Guid.NewGuid(), null));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(new RejectProfilePhotoRequest(Guid.Empty, Guid.NewGuid(), null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RejectProfilePhotoRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyEmployeeId_Fails()
    {
        var result = Validator.Validate(new RejectProfilePhotoRequest(Guid.NewGuid(), Guid.Empty, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RejectProfilePhotoRequest.EmployeeId));
    }

    [Fact]
    public void Validate_EmptyCompanyId_And_EmployeeId_Reports_Both_Errors()
    {
        var result = Validator.Validate(new RejectProfilePhotoRequest(Guid.Empty, Guid.Empty, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RejectProfilePhotoRequest.CompanyId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RejectProfilePhotoRequest.EmployeeId));
    }
}
