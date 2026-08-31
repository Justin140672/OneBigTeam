using HR.Modules.Identity.Features.GetAccessReview;

namespace HR.Modules.Identity.Tests;

public class GetAccessReviewValidatorTests
{
    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new GetAccessReviewValidator();

        var result = validator.Validate(new GetAccessReviewRequest { CompanyId = Guid.NewGuid() });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new GetAccessReviewValidator();

        var result = validator.Validate(new GetAccessReviewRequest { CompanyId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetAccessReviewRequest.CompanyId));
    }
}
