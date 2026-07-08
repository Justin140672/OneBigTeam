using HR.Modules.Leave.Features.GetLeaveBalanceHistory;

namespace HR.Modules.Leave.Tests;

public class GetLeaveBalanceHistoryValidatorTests
{
    private static GetLeaveBalanceHistoryRequest ValidRequest() => new(
        CompanyId: Guid.NewGuid(),
        EmployeeId: Guid.NewGuid(),
        LeaveTypeId: Guid.NewGuid());

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var v = new GetLeaveBalanceHistoryValidator();
        var result = v.Validate(ValidRequest() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetLeaveBalanceHistoryRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_LeaveTypeId_Is_Empty()
    {
        var v = new GetLeaveBalanceHistoryValidator();
        var result = v.Validate(ValidRequest() with { LeaveTypeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetLeaveBalanceHistoryRequest.LeaveTypeId));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var v = new GetLeaveBalanceHistoryValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }
}
