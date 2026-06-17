using HR.Modules.Leave.Features.RejectLeaveRequest;

namespace HR.Modules.Leave.Tests;

public class RejectLeaveRequestValidatorTests
{
    private static RejectLeaveRequestRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        LeaveRequestId = Guid.NewGuid(),
        ReviewedByEmployeeId = Guid.NewGuid()
    };

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var v = new RejectLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RejectLeaveRequestRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var v = new RejectLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RejectLeaveRequestRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_LeaveRequestId_Is_Empty()
    {
        var v = new RejectLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { LeaveRequestId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RejectLeaveRequestRequest.LeaveRequestId));
    }

    [Fact]
    public void Validate_Fails_When_ReviewedByEmployeeId_Is_Empty()
    {
        var v = new RejectLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { ReviewedByEmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RejectLeaveRequestRequest.ReviewedByEmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_RejectionReason_Exceeds_500_Characters()
    {
        var v = new RejectLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { RejectionReason = new string('R', 501) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RejectLeaveRequestRequest.RejectionReason));
    }

    [Fact]
    public void Validate_Passes_When_RejectionReason_Is_Null()
    {
        var v = new RejectLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { RejectionReason = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_RejectionReason_Is_At_Max_Length()
    {
        var v = new RejectLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { RejectionReason = new string('R', 500) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var v = new RejectLeaveRequestValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Passes_With_Rejection_Reason()
    {
        var v = new RejectLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { RejectionReason = "Insufficient cover during this period." });
        Assert.True(result.IsValid);
    }
}
