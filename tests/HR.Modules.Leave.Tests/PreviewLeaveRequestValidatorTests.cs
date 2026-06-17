using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.PreviewLeaveRequest;

namespace HR.Modules.Leave.Tests;

public class PreviewLeaveRequestValidatorTests
{
    private static PreviewLeaveRequestRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        LeaveTypeId = Guid.NewGuid(),
        StartDate = new DateOnly(2026, 8, 10),
        StartPart = LeaveDayPart.FullDay,
        EndDate = new DateOnly(2026, 8, 14),
        EndPart = LeaveDayPart.FullDay
    };

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var v = new PreviewLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PreviewLeaveRequestRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var v = new PreviewLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PreviewLeaveRequestRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_LeaveTypeId_Is_Empty()
    {
        var v = new PreviewLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { LeaveTypeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PreviewLeaveRequestRequest.LeaveTypeId));
    }

    [Fact]
    public void Validate_Fails_When_StartDate_Is_MinValue()
    {
        var v = new PreviewLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { StartDate = DateOnly.MinValue });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PreviewLeaveRequestRequest.StartDate));
    }

    [Fact]
    public void Validate_Fails_When_EndDate_Is_MinValue()
    {
        var v = new PreviewLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { EndDate = DateOnly.MinValue });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PreviewLeaveRequestRequest.EndDate));
    }

    [Fact]
    public void Validate_Fails_When_StartPart_Is_Invalid_Enum()
    {
        var v = new PreviewLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { StartPart = (LeaveDayPart)99 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PreviewLeaveRequestRequest.StartPart));
    }

    [Fact]
    public void Validate_Fails_When_EndPart_Is_Invalid_Enum()
    {
        var v = new PreviewLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with { EndPart = (LeaveDayPart)99 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PreviewLeaveRequestRequest.EndPart));
    }

    [Fact]
    public void Validate_Fails_When_EndDate_Is_Before_StartDate()
    {
        var v = new PreviewLeaveRequestValidator();
        var result = v.Validate(ValidRequest() with
        {
            StartDate = new DateOnly(2026, 8, 14),
            EndDate = new DateOnly(2026, 8, 10)
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PreviewLeaveRequestRequest.EndDate));
    }

    [Fact]
    public void Validate_Passes_When_EndDate_Equals_StartDate()
    {
        var v = new PreviewLeaveRequestValidator();
        var date = new DateOnly(2026, 8, 10);
        var result = v.Validate(ValidRequest() with { StartDate = date, EndDate = date });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Half_Day_Request()
    {
        var v = new PreviewLeaveRequestValidator();
        var date = new DateOnly(2026, 8, 10);
        var result = v.Validate(ValidRequest() with
        {
            StartDate = date,
            StartPart = LeaveDayPart.Morning,
            EndDate = date,
            EndPart = LeaveDayPart.Morning
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Valid_Multi_Day_Request()
    {
        var v = new PreviewLeaveRequestValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }
}
