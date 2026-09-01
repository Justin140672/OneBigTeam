using HR.Modules.Leave.Features.ApproveLeaveRequest;

namespace HR.Modules.Leave.Tests;

public class ApproveLeaveRequestValidatorTests
{
    private static ApproveLeaveRequestRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        LeaveRequestId = Guid.NewGuid(),
        ReviewedByEmployeeId = Guid.NewGuid()
    };

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var v = new ApproveLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApproveLeaveRequestRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var v = new ApproveLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApproveLeaveRequestRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_LeaveRequestId_Is_Empty()
    {
        var v = new ApproveLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { LeaveRequestId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ApproveLeaveRequestRequest.LeaveRequestId));
    }

    [Fact]
    public void Validate_Passes_When_ReviewedByEmployeeId_Is_Empty()
    {
        // ReviewedByEmployeeId is not client input — the endpoint overwrites it with the
        // authenticated caller before authorization — so the validator must not reject an empty
        // value (doing so pre-empted the resource-authorization check).
        var v = new ApproveLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { ReviewedByEmployeeId = Guid.Empty });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var v = new ApproveLeaveRequestValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }
}
