using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateEmployeeProfileAndEmployment;

namespace HR.Modules.Employees.Tests;

public class UpdateEmployeeProfileAndEmploymentValidatorTests
{
    private readonly UpdateEmployeeProfileAndEmploymentValidator _validator = new();

    private static UpdateEmployeeProfileAndEmploymentRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        FirstName = "Alice",
        LastName = "Smith",
        WorkEmail = "alice@example.com",
        EmployeeNumber = "EMP-001",
        Status = EmploymentStatus.Active,
        StartDate = new DateOnly(2026, 7, 1),
        ExpectedVersion = 1,
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
        => Assert.True(_validator.Validate(ValidRequest()).IsValid);

    [Fact]
    public void Validate_Fails_When_ExpectedVersion_Is_Null()
    {
        var result = _validator.Validate(ValidRequest() with { ExpectedVersion = null });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeProfileAndEmploymentRequest.ExpectedVersion));
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { Id = Guid.Empty });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_FirstName_Is_Whitespace()
    {
        var result = _validator.Validate(ValidRequest() with { FirstName = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeProfileAndEmploymentRequest.FirstName));
    }

    [Fact]
    public void Validate_Passes_When_FirstName_Is_Exactly_100_Chars()
        => Assert.True(_validator.Validate(ValidRequest() with { FirstName = new string('A', 100) }).IsValid);

    [Fact]
    public void Validate_Fails_When_FirstName_Is_101_Chars()
        => Assert.False(_validator.Validate(ValidRequest() with { FirstName = new string('A', 101) }).IsValid);

    [Fact]
    public void Validate_Fails_When_WorkEmail_Is_Invalid()
        => Assert.False(_validator.Validate(ValidRequest() with { WorkEmail = "not-an-email" }).IsValid);

    [Fact]
    public void Validate_Fails_When_EmployeeNumber_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { EmployeeNumber = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEmployeeProfileAndEmploymentRequest.EmployeeNumber));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeNumber_Has_Invalid_Format()
        => Assert.False(_validator.Validate(ValidRequest() with { EmployeeNumber = "EMP@001!" }).IsValid);

    [Fact]
    public void Validate_Fails_When_StartDate_Is_Default()
        => Assert.False(_validator.Validate(ValidRequest() with { StartDate = default }).IsValid);

    [Fact]
    public void Validate_Fails_When_Only_NoticePeriodUnitOverride_Is_Set()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            NoticePeriodUnitOverride = NoticePeriodUnit.Weeks,
            NoticePeriodLengthOverride = null,
        });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Only_NoticePeriodLengthOverride_Is_Set()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            NoticePeriodUnitOverride = null,
            NoticePeriodLengthOverride = 4,
        });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Both_NoticePeriodOverrides_Are_Set()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            NoticePeriodUnitOverride = NoticePeriodUnit.Weeks,
            NoticePeriodLengthOverride = 4,
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_HoursPerDayOverride_Exceeds_24()
        => Assert.False(_validator.Validate(ValidRequest() with { HoursPerDayOverride = 24.1m }).IsValid);

    [Fact]
    public void Validate_Fails_When_HoursPerDayOverride_Is_Zero()
        => Assert.False(_validator.Validate(ValidRequest() with { HoursPerDayOverride = 0m }).IsValid);

    [Fact]
    public void Validate_Fails_When_WorkingDaysOverride_Is_None()
        => Assert.False(_validator.Validate(ValidRequest() with { WorkingDaysOverride = WorkingDays.None }).IsValid);
}
