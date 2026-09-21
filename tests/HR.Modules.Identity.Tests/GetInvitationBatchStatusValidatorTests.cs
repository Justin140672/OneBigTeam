using HR.Modules.Identity.Features.GetInvitationBatchStatus;

namespace HR.Modules.Identity.Tests;

public class GetInvitationBatchStatusValidatorTests
{
    private readonly GetInvitationBatchStatusValidator _validator = new();

    [Fact]
    public void Valid_Request_Passes()
    {
        var result = _validator.Validate(new GetInvitationBatchStatusRequest(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Empty_CompanyId_Fails()
    {
        var result = _validator.Validate(new GetInvitationBatchStatusRequest(Guid.Empty, Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetInvitationBatchStatusRequest.CompanyId));
    }

    [Fact]
    public void Empty_BatchId_Fails()
    {
        var result = _validator.Validate(new GetInvitationBatchStatusRequest(Guid.NewGuid(), Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetInvitationBatchStatusRequest.BatchId));
    }
}
