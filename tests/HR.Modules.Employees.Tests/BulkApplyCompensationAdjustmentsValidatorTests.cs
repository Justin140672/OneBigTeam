using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.BulkApplyCompensationAdjustments;

namespace HR.Modules.Employees.Tests;

public class BulkApplyCompensationAdjustmentsValidatorTests
{
    private readonly BulkApplyCompensationAdjustmentsValidator _validator = new();

    private static BulkApplyCompensationAdjustmentsRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EffectiveDate = new DateOnly(2026, 1, 1),
        Reason = CompensationChangeReason.AnnualReview,
        AdjustmentMode = CompensationAdjustmentMode.PercentageIncrease,
        Items =
        [
            new BulkCompensationAdjustmentItem { EmployeeId = Guid.NewGuid(), ProposedSalary = 45000m, SalaryType = SalaryType.Annual, Currency = "GBP" }
        ]
    };

    [Fact]
    public void Valid_Request_Passes()
    {
        var result = _validator.Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Rejects_Empty_CompanyId()
    {
        var request = ValidRequest() with { CompanyId = Guid.Empty };
        Assert.False(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Rejects_Empty_EffectiveDate()
    {
        var request = ValidRequest() with { EffectiveDate = default };
        Assert.False(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Rejects_Invalid_Reason_Enum_Value()
    {
        var request = ValidRequest() with { Reason = (CompensationChangeReason)999 };
        Assert.False(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Rejects_Invalid_AdjustmentMode_Enum_Value()
    {
        var request = ValidRequest() with { AdjustmentMode = (CompensationAdjustmentMode)999 };
        Assert.False(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Rejects_Empty_Items()
    {
        var request = ValidRequest() with { Items = [] };
        Assert.False(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Rejects_Duplicate_EmployeeId_Within_Items()
    {
        var employeeId = Guid.NewGuid();
        var request = ValidRequest() with
        {
            Items =
            [
                new BulkCompensationAdjustmentItem { EmployeeId = employeeId, ProposedSalary = 45000m, SalaryType = SalaryType.Annual, Currency = "GBP" },
                new BulkCompensationAdjustmentItem { EmployeeId = employeeId, ProposedSalary = 46000m, SalaryType = SalaryType.Annual, Currency = "GBP" }
            ]
        };

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Rejects_Empty_Item_EmployeeId()
    {
        var request = ValidRequest() with
        {
            Items = [new BulkCompensationAdjustmentItem { EmployeeId = Guid.Empty, ProposedSalary = 45000m, SalaryType = SalaryType.Annual, Currency = "GBP" }]
        };

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Rejects_Item_ProposedSalary_Not_Greater_Than_Zero(decimal salary)
    {
        var request = ValidRequest() with
        {
            Items = [new BulkCompensationAdjustmentItem { EmployeeId = Guid.NewGuid(), ProposedSalary = salary, SalaryType = SalaryType.Annual, Currency = "GBP" }]
        };

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Rejects_Item_Invalid_SalaryType_Enum_Value()
    {
        var request = ValidRequest() with
        {
            Items = [new BulkCompensationAdjustmentItem { EmployeeId = Guid.NewGuid(), ProposedSalary = 45000m, SalaryType = (SalaryType)999, Currency = "GBP" }]
        };

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("GB")]
    [InlineData("GBPX")]
    public void Rejects_Item_Currency_Not_Exactly_Three_Characters(string currency)
    {
        var request = ValidRequest() with
        {
            Items = [new BulkCompensationAdjustmentItem { EmployeeId = Guid.NewGuid(), ProposedSalary = 45000m, SalaryType = SalaryType.Annual, Currency = currency }]
        };

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Rejects_Item_HoursPerWeek_Not_Greater_Than_Zero_When_Provided(decimal hoursPerWeek)
    {
        var request = ValidRequest() with
        {
            Items = [new BulkCompensationAdjustmentItem { EmployeeId = Guid.NewGuid(), ProposedSalary = 45000m, SalaryType = SalaryType.Annual, Currency = "GBP", HoursPerWeek = hoursPerWeek }]
        };

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Rejects_Item_FTE_Outside_Zero_To_One_Range_When_Provided(decimal fte)
    {
        var request = ValidRequest() with
        {
            Items = [new BulkCompensationAdjustmentItem { EmployeeId = Guid.NewGuid(), ProposedSalary = 45000m, SalaryType = SalaryType.Annual, Currency = "GBP", FTE = fte }]
        };

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0.5)]
    public void Accepts_Item_FTE_At_Boundaries(decimal fte)
    {
        var request = ValidRequest() with
        {
            Items = [new BulkCompensationAdjustmentItem { EmployeeId = Guid.NewGuid(), ProposedSalary = 45000m, SalaryType = SalaryType.Annual, Currency = "GBP", FTE = fte }]
        };

        Assert.True(_validator.Validate(request).IsValid);
    }
}
