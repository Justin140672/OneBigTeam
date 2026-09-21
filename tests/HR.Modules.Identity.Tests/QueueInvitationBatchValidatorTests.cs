using HR.Modules.Identity.Features.QueueInvitationBatch;

namespace HR.Modules.Identity.Tests;

public class QueueInvitationBatchValidatorTests
{
    private readonly QueueInvitationBatchValidator _validator = new();

    [Fact]
    public void Valid_Request_Passes()
    {
        var request = new QueueInvitationBatchRequest(Guid.NewGuid(), [Guid.NewGuid()]);

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Empty_CompanyId_Fails()
    {
        var request = new QueueInvitationBatchRequest(Guid.Empty, [Guid.NewGuid()]);

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(QueueInvitationBatchRequest.CompanyId));
    }

    [Fact]
    public void Empty_EmployeeIds_List_Fails()
    {
        var request = new QueueInvitationBatchRequest(Guid.NewGuid(), []);

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(QueueInvitationBatchRequest.EmployeeIds));
    }

    [Fact]
    public void Null_EmployeeIds_List_Fails()
    {
        var request = new QueueInvitationBatchRequest(Guid.NewGuid(), null!);

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(QueueInvitationBatchRequest.EmployeeIds));
    }
}
