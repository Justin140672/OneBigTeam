using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateCompensationRecord;

namespace HR.Modules.Employees.Tests;

public class CreateCompensationRecordValidatorTests
{
    private static CreateCompensationRecordRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        EffectiveFrom = new DateOnly(2026, 1, 1),
        SalaryType = SalaryType.Annual,
        Salary = 45000m,
        Currency = "GBP",
        Reason = CompensationChangeReason.NewHire
    };

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var v = new CreateCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompensationRecordRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var v = new CreateCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompensationRecordRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_EffectiveFrom_Is_Not_Provided()
    {
        var v = new CreateCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { EffectiveFrom = default });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompensationRecordRequest.EffectiveFrom));
    }

    [Fact]
    public void Validate_Fails_When_Salary_Is_Zero_Or_Negative()
    {
        var v = new CreateCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { Salary = 0 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompensationRecordRequest.Salary));
    }

    [Fact]
    public void Validate_Fails_When_Currency_Is_Not_Three_Characters()
    {
        var v = new CreateCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { Currency = "POUND" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompensationRecordRequest.Currency));
    }

    [Fact]
    public void Validate_Fails_When_SalaryType_Is_Not_A_Defined_Value()
    {
        var v = new CreateCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { SalaryType = (SalaryType)999 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompensationRecordRequest.SalaryType));
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void Validate_Fails_When_FTE_Out_Of_Range(decimal fte)
    {
        var v = new CreateCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { FTE = fte });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompensationRecordRequest.FTE));
    }

    [Fact]
    public void Validate_Fails_When_HoursPerWeek_Is_Zero()
    {
        var v = new CreateCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { HoursPerWeek = 0 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompensationRecordRequest.HoursPerWeek));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Not_A_Defined_Value()
    {
        var v = new CreateCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { Reason = (CompensationChangeReason)999 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompensationRecordRequest.Reason));
    }

    [Fact]
    public void Validate_Passes_For_Minimal_Valid_Request()
    {
        var v = new CreateCompensationRecordValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Full_Valid_Request()
    {
        var v = new CreateCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with
        {
            HoursPerWeek = 37.5m,
            FTE = 1m,
            Notes = "Annual review increase"
        });
        Assert.True(result.IsValid);
    }
}
