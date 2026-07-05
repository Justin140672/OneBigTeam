using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.UpdateFutureCompensationRecord;

namespace HR.Modules.Employees.Tests;

public class UpdateFutureCompensationRecordValidatorTests
{
    private static UpdateFutureCompensationRecordRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        Id = Guid.NewGuid(),
        SalaryType = SalaryType.Annual,
        Salary = 45000m,
        Currency = "GBP"
    };

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var v = new UpdateFutureCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateFutureCompensationRecordRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var v = new UpdateFutureCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateFutureCompensationRecordRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_Id_Is_Empty()
    {
        var v = new UpdateFutureCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { Id = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateFutureCompensationRecordRequest.Id));
    }

    [Fact]
    public void Validate_Fails_When_Salary_Is_Zero_Or_Negative()
    {
        var v = new UpdateFutureCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { Salary = 0 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateFutureCompensationRecordRequest.Salary));
    }

    [Fact]
    public void Validate_Fails_When_Currency_Is_Not_Three_Characters()
    {
        var v = new UpdateFutureCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { Currency = "POUND" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateFutureCompensationRecordRequest.Currency));
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void Validate_Fails_When_FTE_Out_Of_Range(decimal fte)
    {
        var v = new UpdateFutureCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with { FTE = fte });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateFutureCompensationRecordRequest.FTE));
    }

    [Fact]
    public void Validate_Passes_For_Minimal_Valid_Request()
    {
        var v = new UpdateFutureCompensationRecordValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Full_Valid_Request()
    {
        var v = new UpdateFutureCompensationRecordValidator();
        var result = v.Validate(ValidRequest() with
        {
            HoursPerWeek = 37.5m,
            FTE = 1m,
            Notes = "Corrected salary figure"
        });
        Assert.True(result.IsValid);
    }
}
