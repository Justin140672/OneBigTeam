using HR.Modules.Identity.Features.RetryInvitationBatch;

namespace HR.Modules.Identity.Tests;

public class RetryInvitationBatchValidatorTests
{
    private readonly RetryInvitationBatchValidator _validator = new();

    [Fact]
    public void Valid_Request_Passes()
    {
        var result = _validator.Validate(new RetryInvitationBatchRequest(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Empty_CompanyId_Fails()
    {
        var result = _validator.Validate(new RetryInvitationBatchRequest(Guid.Empty, Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RetryInvitationBatchRequest.CompanyId));
    }

    [Fact]
    public void Empty_BatchId_Fails()
    {
        var result = _validator.Validate(new RetryInvitationBatchRequest(Guid.NewGuid(), Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RetryInvitationBatchRequest.BatchId));
    }
}
