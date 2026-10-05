using System.ComponentModel.DataAnnotations;
using HR.Web.Models;

namespace HR.Web.Tests;

public class EmployeeCompensationValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static EmployeeProfileEditModel ValidModel(bool compensationRequired) => new()
    {
        FirstName = "Alice",
        LastName = "Smith",
        WorkEmail = "alice@example.com",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Nationality = "British",
        Gender = "Female",
        EmploymentTypeId = Guid.NewGuid(),
        DepartmentId = Guid.NewGuid(),
        LocationId = Guid.NewGuid(),
        PositionProfileId = Guid.NewGuid(),
        EmployeeNumberAutoAssigned = true,
        CompensationRequired = compensationRequired,
        Salary = 45000m,
    };

    [Fact]
    public void Add_Flow_Defaults_To_Annual_And_Gbp()
    {
        var model = new EmployeeProfileEditModel();

        Assert.Equal("Annual", model.SalaryType);
        Assert.Equal("GBP", model.Currency);
    }

    [Fact]
    public void Add_Flow_Requires_A_Salary()
    {
        var model = ValidModel(compensationRequired: true);
        model.Salary = null;

        var results = Validate(model);

        Assert.Contains(results, r =>
            r.MemberNames.Contains(nameof(EmployeeProfileEditModel.Salary)) && r.ErrorMessage == "Please enter a salary.");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Add_Flow_Rejects_A_NonPositive_Salary(string salary)
    {
        var model = ValidModel(compensationRequired: true);
        model.Salary = decimal.Parse(salary);

        var results = Validate(model);

        Assert.Contains(results, r =>
            r.MemberNames.Contains(nameof(EmployeeProfileEditModel.Salary)) && r.ErrorMessage == "Salary must be greater than 0.");
    }

    [Fact]
    public void Add_Flow_Accepts_The_Smallest_Positive_Salary()
    {
        var model = ValidModel(compensationRequired: true);
        model.Salary = 0.01m;

        Assert.Empty(Validate(model));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_Flow_Requires_A_Currency(string currency)
    {
        var model = ValidModel(compensationRequired: true);
        model.Currency = currency;

        var results = Validate(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EmployeeProfileEditModel.Currency)));
    }

    [Theory]
    [InlineData("GB")]
    [InlineData("GBPP")]
    public void Add_Flow_Rejects_A_Currency_That_Is_Not_Three_Letters(string currency)
    {
        var model = ValidModel(compensationRequired: true);
        model.Currency = currency;

        var results = Validate(model);

        Assert.Contains(results, r =>
            r.MemberNames.Contains(nameof(EmployeeProfileEditModel.Currency))
            && r.ErrorMessage == "Currency must be a 3-letter code (e.g. GBP).");
    }

    [Fact]
    public void Add_Flow_Accepts_A_Complete_Compensation()
    {
        var model = ValidModel(compensationRequired: true);
        model.SalaryType = "Hourly";
        model.Salary = 25.5m;
        model.Currency = "USD";

        Assert.Empty(Validate(model));
    }

    [Fact]
    public void Edit_Flow_Does_Not_Require_A_Salary()
    {
        var model = ValidModel(compensationRequired: false);
        model.Salary = null;

        Assert.Empty(Validate(model));
    }
}
