using HR.Modules.Support.Domain;
using HR.Modules.Support.Features.UpdateSupportRequestStatus;

namespace HR.Modules.Support.Tests;

public class UpdateSupportRequestStatusValidatorTests
{
    private readonly UpdateSupportRequestStatusValidator _validator = new();

    private static UpdateSupportRequestStatusRequest Valid() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        Status = SupportRequestStatus.UnderReview,
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var request = Valid();
        request = request with { CompanyId = Guid.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateSupportRequestStatusRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var request = Valid();
        request = request with { Id = Guid.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateSupportRequestStatusRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_Status_Is_Not_A_Defined_Enum_Value()
    {
        var request = Valid();
        request = request with { Status = (SupportRequestStatus)999 };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateSupportRequestStatusRequest.Status));
    }
}
