using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.SubmitLeaveRequest;

namespace HR.Modules.Leave.Tests;

public class SubmitLeaveRequestValidatorTests
{
    private readonly SubmitLeaveRequestValidator _validator = new();

    private static SubmitLeaveRequestRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        LeaveTypeId = Guid.NewGuid(),
        StartDate = new DateOnly(2026, 8, 3),   // Monday
        StartPart = LeaveDayPart.FullDay,
        EndDate = new DateOnly(2026, 8, 7),     // Friday
        EndPart = LeaveDayPart.FullDay,
        Reason = null
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        Assert.True(_validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Start_And_End_Are_Same_Day()
    {
        var request = ValidRequest() with { StartDate = new DateOnly(2026, 8, 3), EndDate = new DateOnly(2026, 8, 3) };
        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        Assert.False(_validator.Validate(ValidRequest() with { CompanyId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        Assert.False(_validator.Validate(ValidRequest() with { EmployeeId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_LeaveTypeId_Is_Empty()
    {
        Assert.False(_validator.Validate(ValidRequest() with { LeaveTypeId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_StartDate_Is_MinValue()
    {
        Assert.False(_validator.Validate(ValidRequest() with { StartDate = DateOnly.MinValue }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EndDate_Is_MinValue()
    {
        Assert.False(_validator.Validate(ValidRequest() with { EndDate = DateOnly.MinValue }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EndDate_Is_Before_StartDate()
    {
        var request = ValidRequest() with { StartDate = new DateOnly(2026, 8, 7), EndDate = new DateOnly(2026, 8, 3) };
        var result = _validator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SubmitLeaveRequestRequest.EndDate));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Exceeds_1000_Characters()
    {
        Assert.False(_validator.Validate(ValidRequest() with { Reason = new string('x', 1001) }).IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Reason_Is_Exactly_1000_Characters()
    {
        Assert.True(_validator.Validate(ValidRequest() with { Reason = new string('x', 1000) }).IsValid);
    }
}
