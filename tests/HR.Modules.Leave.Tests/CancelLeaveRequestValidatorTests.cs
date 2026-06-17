using HR.Modules.Leave.Features.CancelLeaveRequest;

namespace HR.Modules.Leave.Tests;

public class CancelLeaveRequestValidatorTests
{
    private static CancelLeaveRequestRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        LeaveRequestId = Guid.NewGuid()
    };

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var v = new CancelLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CancelLeaveRequestRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var v = new CancelLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CancelLeaveRequestRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_LeaveRequestId_Is_Empty()
    {
        var v = new CancelLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { LeaveRequestId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CancelLeaveRequestRequest.LeaveRequestId));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var v = new CancelLeaveRequestValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }
}
