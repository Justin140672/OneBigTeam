using HR.Modules.Leave.Features.GetEmployeeLeaveBalance;

namespace HR.Modules.Leave.Tests;

public class GetEmployeeLeaveBalanceValidatorTests
{
    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new GetEmployeeLeaveBalanceValidator();

        var result = validator.Validate(new GetEmployeeLeaveBalanceRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            PolicyYear = 2026
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var validator = new GetEmployeeLeaveBalanceValidator();

        var result = validator.Validate(new GetEmployeeLeaveBalanceRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.Empty,
            PolicyYear = 2026
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeLeaveBalanceRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_PolicyYear_Is_Below_2000()
    {
        var validator = new GetEmployeeLeaveBalanceValidator();

        var result = validator.Validate(new GetEmployeeLeaveBalanceRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            PolicyYear = 1999
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeLeaveBalanceRequest.PolicyYear));
    }

    [Fact]
    public void Validate_Fails_When_PolicyYear_Is_Above_2100()
    {
        var validator = new GetEmployeeLeaveBalanceValidator();

        var result = validator.Validate(new GetEmployeeLeaveBalanceRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            PolicyYear = 2101
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetEmployeeLeaveBalanceRequest.PolicyYear));
    }
}
