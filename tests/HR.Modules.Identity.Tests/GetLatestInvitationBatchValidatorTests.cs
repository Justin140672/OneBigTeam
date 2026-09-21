using HR.Modules.Identity.Features.GetLatestInvitationBatch;

namespace HR.Modules.Identity.Tests;

public class GetLatestInvitationBatchValidatorTests
{
    private readonly GetLatestInvitationBatchValidator _validator = new();

    [Fact]
    public void Valid_Request_Passes()
    {
        var result = _validator.Validate(new GetLatestInvitationBatchRequest(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Empty_CompanyId_Fails()
    {
        var result = _validator.Validate(new GetLatestInvitationBatchRequest(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetLatestInvitationBatchRequest.CompanyId));
    }
}
