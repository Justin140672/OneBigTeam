using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.PromoteEmployee;

namespace HR.Modules.Employees.Tests;

public class PromoteEmployeeValidatorTests
{
    private static PromoteEmployeeRequest ValidRequest() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            EffectiveDate: new DateOnly(2026, 8, 1),
            Reason: "Outstanding performance.",
            Notes: null,
            NewManagerId: null,
            NewLocationId: null);

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new PromoteEmployeeValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new PromoteEmployeeValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PromoteEmployeeRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var validator = new PromoteEmployeeValidator();
        var request = ValidRequest() with { EmployeeId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PromoteEmployeeRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_NewPositionProfileId_Is_Empty()
    {
        var validator = new PromoteEmployeeValidator();
        var request = ValidRequest() with { NewPositionProfileId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PromoteEmployeeRequest.NewPositionProfileId));
    }

    [Fact]
    public void Validate_Fails_When_EffectiveDate_Is_Default()
    {
        var validator = new PromoteEmployeeValidator();
        var request = ValidRequest() with { EffectiveDate = default };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PromoteEmployeeRequest.EffectiveDate));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Empty()
    {
        var validator = new PromoteEmployeeValidator();
        var request = ValidRequest() with { Reason = string.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PromoteEmployeeRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Exceeds_MaxLength()
    {
        var validator = new PromoteEmployeeValidator();
        var request = ValidRequest() with { Reason = new string('a', 501) };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PromoteEmployeeRequest.Reason));
    }

    [Fact]
    public void Validate_Passes_Without_Compensation_Fields_When_CreateCompensationChange_Is_False()
    {
        var validator = new PromoteEmployeeValidator();
        var request = ValidRequest() with
        {
            CreateCompensationChange = false,
            CompensationSalaryType = null,
            CompensationSalary = null,
            CompensationCurrency = null,
        };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CreateCompensationChange_True_And_CompensationSalaryType_Missing()
    {
        var validator = new PromoteEmployeeValidator();
        var request = ValidRequest() with
        {
            CreateCompensationChange = true,
            CompensationSalaryType = null,
            CompensationSalary = 50000m,
            CompensationCurrency = "GBP",
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PromoteEmployeeRequest.CompensationSalaryType));
    }

    [Fact]
    public void Validate_Fails_When_CreateCompensationChange_True_And_CompensationSalary_Missing()
    {
        var validator = new PromoteEmployeeValidator();
        var request = ValidRequest() with
        {
            CreateCompensationChange = true,
            CompensationSalaryType = SalaryType.Annual,
            CompensationSalary = null,
            CompensationCurrency = "GBP",
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PromoteEmployeeRequest.CompensationSalary));
    }

    [Fact]
    public void Validate_Fails_When_CreateCompensationChange_True_And_CompensationSalary_Is_Zero_Or_Negative()
    {
        var validator = new PromoteEmployeeValidator();
        var request = ValidRequest() with
        {
            CreateCompensationChange = true,
            CompensationSalaryType = SalaryType.Annual,
            CompensationSalary = 0m,
            CompensationCurrency = "GBP",
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PromoteEmployeeRequest.CompensationSalary));
    }

    [Fact]
    public void Validate_Fails_When_CreateCompensationChange_True_And_CompensationCurrency_Missing()
    {
        var validator = new PromoteEmployeeValidator();
        var request = ValidRequest() with
        {
            CreateCompensationChange = true,
            CompensationSalaryType = SalaryType.Annual,
            CompensationSalary = 50000m,
            CompensationCurrency = null,
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PromoteEmployeeRequest.CompensationCurrency));
    }

    [Fact]
    public void Validate_Passes_When_CreateCompensationChange_True_And_All_Compensation_Fields_Provided()
    {
        var validator = new PromoteEmployeeValidator();
        var request = ValidRequest() with
        {
            CreateCompensationChange = true,
            CompensationSalaryType = SalaryType.Annual,
            CompensationSalary = 60000m,
            CompensationCurrency = "GBP",
        };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }
}
