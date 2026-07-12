using HR.Modules.Documents.Features.CancelPendingProfilePhoto;

namespace HR.Modules.Documents.Tests;

public class CancelPendingProfilePhotoValidatorTests
{
    private static readonly CancelPendingProfilePhotoValidator Validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(new CancelPendingProfilePhotoRequest(Guid.NewGuid()));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(new CancelPendingProfilePhotoRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CancelPendingProfilePhotoRequest.CompanyId));
    }
}
