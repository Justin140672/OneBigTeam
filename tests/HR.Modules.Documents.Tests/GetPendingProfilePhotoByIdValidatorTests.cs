using HR.Modules.Documents.Features.GetPendingProfilePhotoById;

namespace HR.Modules.Documents.Tests;

public class GetPendingProfilePhotoByIdValidatorTests
{
    private static readonly GetPendingProfilePhotoByIdValidator Validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var result = Validator.Validate(new GetPendingProfilePhotoByIdRequest(Guid.NewGuid(), Guid.NewGuid()));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyCompanyId_Fails()
    {
        var result = Validator.Validate(new GetPendingProfilePhotoByIdRequest(Guid.Empty, Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetPendingProfilePhotoByIdRequest.CompanyId));
    }

    [Fact]
    public void Validate_EmptyPendingPhotoId_Fails()
    {
        var result = Validator.Validate(new GetPendingProfilePhotoByIdRequest(Guid.NewGuid(), Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetPendingProfilePhotoByIdRequest.PendingPhotoId));
    }

    [Fact]
    public void Validate_EmptyCompanyId_And_PendingPhotoId_Reports_Both_Errors()
    {
        var result = Validator.Validate(new GetPendingProfilePhotoByIdRequest(Guid.Empty, Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetPendingProfilePhotoByIdRequest.CompanyId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetPendingProfilePhotoByIdRequest.PendingPhotoId));
    }
}
