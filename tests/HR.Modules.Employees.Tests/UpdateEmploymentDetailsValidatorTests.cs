using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateEmploymentDetails;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class UpdateEmploymentDetailsValidatorTests
{
    private readonly UpdateEmploymentDetailsValidator _validator = new();

    private static UpdateEmploymentDetailsRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        EmployeeNumber = "EMP-001",
        EmploymentTypeId = Guid.NewGuid(),
        Status = EmploymentStatus.Active,
        StartDate = new DateOnly(2026, 1, 1)
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EmployeeNumber_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { EmployeeNumber = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.EmployeeNumber));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeNumber_Exceeds_50_Characters()
    {
        var result = _validator.Validate(ValidRequest() with { EmployeeNumber = new string('X', 51) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.EmployeeNumber));
    }

    // ── employee number format — same regex CreateEmployee applies (Wave 1) ─────

    [Theory]
    [InlineData("EMP-001")]
    [InlineData("EMP_001")]
    [InlineData("EMP.001")]
    [InlineData("EMP/001")]
    [InlineData("EMP 001")]
    [InlineData("abc123")]
    [InlineData("123456")]
    public void Validate_Passes_For_Valid_EmployeeNumber_Formats(string employeeNumber)
    {
        var result = _validator.Validate(ValidRequest() with { EmployeeNumber = employeeNumber });
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("EMP@001")]
    [InlineData("EMP#001")]
    [InlineData("EMP!001")]
    [InlineData("EMP*001")]
    [InlineData("EMP+001")]
    [InlineData("EMP%001")]
    public void Validate_Fails_For_Invalid_EmployeeNumber_Formats(string employeeNumber)
    {
        var result = _validator.Validate(ValidRequest() with { EmployeeNumber = employeeNumber });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.EmployeeNumber));
    }

    [Fact]
    public void Validate_Passes_When_EmployeeNumber_Is_Exactly_50_Characters()
    {
        var result = _validator.Validate(ValidRequest() with { EmployeeNumber = new string('A', 50) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_EmploymentTypeId_Is_Null()
    {
        var result = _validator.Validate(ValidRequest() with { EmploymentTypeId = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EmploymentTypeId_Is_Empty_Guid()
    {
        var result = _validator.Validate(ValidRequest() with { EmploymentTypeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.EmploymentTypeId));
    }

    // Status == Draft is deliberately NOT rejected here — see UpdateEmploymentDetailsHandlerTests
    // for the Draft-transition check, which needs the employee's *current* status (only available
    // in the handler, not the request-shape-only validator) to tell "still Draft, unrelated edit"
    // apart from "actively reverting back to Draft".

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { Id = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_HoursPerDayOverride_Is_Zero()
    {
        var result = _validator.Validate(ValidRequest() with { HoursPerDayOverride = 0m });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.HoursPerDayOverride));
    }

    [Fact]
    public void Validate_Fails_When_HoursPerDayOverride_Exceeds_24()
    {
        var result = _validator.Validate(ValidRequest() with { HoursPerDayOverride = 24.1m });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.HoursPerDayOverride));
    }

    [Fact]
    public void Validate_Fails_When_WorkingDaysOverride_Is_None()
    {
        var result = _validator.Validate(ValidRequest() with { WorkingDaysOverride = WorkingDays.None });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.WorkingDaysOverride));
    }

    // ── notice period override (both-or-neither) ────────────────────────────────

    [Fact]
    public void Validate_Passes_When_NoticePeriodOverride_Is_Entirely_Absent()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            NoticePeriodUnitOverride = null,
            NoticePeriodLengthOverride = null
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_NoticePeriodOverride_Unit_And_Length_Are_Both_Set()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            NoticePeriodUnitOverride = NoticePeriodUnit.Weeks,
            NoticePeriodLengthOverride = 4
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_NoticePeriodUnitOverride_Set_Without_Length()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            NoticePeriodUnitOverride = NoticePeriodUnit.Months,
            NoticePeriodLengthOverride = null
        });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_NoticePeriodLengthOverride_Set_Without_Unit()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            NoticePeriodUnitOverride = null,
            NoticePeriodLengthOverride = 4
        });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_NoticePeriodLengthOverride_Is_Zero()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            NoticePeriodUnitOverride = NoticePeriodUnit.Months,
            NoticePeriodLengthOverride = 0
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.NoticePeriodLengthOverride));
    }

    [Fact]
    public void Validate_Fails_When_NoticePeriodLengthOverride_Is_Negative()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            NoticePeriodUnitOverride = NoticePeriodUnit.Months,
            NoticePeriodLengthOverride = -1
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmploymentDetailsRequest.NoticePeriodLengthOverride));
    }
}
