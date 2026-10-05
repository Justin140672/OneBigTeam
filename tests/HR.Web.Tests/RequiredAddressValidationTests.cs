using System.ComponentModel.DataAnnotations;
using HR.Web.Models;

namespace HR.Web.Tests;

public class RequiredAddressValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static EmployeeProfileEditModel ValidModel(bool addressRequired) => new()
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
        AddressRequired = addressRequired,
    };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_Flow_Rejects_Blank_Or_Whitespace_Required_Address_Fields(string blank)
    {
        var model = ValidModel(addressRequired: true);
        model.AddressLine1 = blank;
        model.City = blank;
        model.PostCode = blank;

        var results = Validate(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EmployeeProfileEditModel.AddressLine1)) && r.ErrorMessage == "Address line 1 is required.");
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EmployeeProfileEditModel.City)) && r.ErrorMessage == "City is required.");
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EmployeeProfileEditModel.PostCode)) && r.ErrorMessage == "Postcode is required.");
    }

    [Fact]
    public void Add_Flow_Accepts_A_Complete_Address_With_Optional_Fields_Blank()
    {
        var model = ValidModel(addressRequired: true);
        model.AddressLine1 = "1 High Street";
        model.City = "London";
        model.PostCode = "SW1A 1AA";

        Assert.Empty(Validate(model));
    }

    [Fact]
    public void Edit_Flow_Does_Not_Newly_Require_The_Address_For_Existing_Employees()
    {
        var model = ValidModel(addressRequired: false);

        Assert.Empty(Validate(model));
    }

    [Fact]
    public void VacancyEditModel_Requires_An_EmploymentType()
    {
        var model = new VacancyEditModel
        {
            PositionProfileId = Guid.NewGuid(),
            HiringManagerId = Guid.NewGuid(),
        };

        var results = Validate(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(VacancyEditModel.EmploymentTypeId)) && r.ErrorMessage == "Employment type is required.");

        model.EmploymentTypeId = Guid.NewGuid();
        Assert.Empty(Validate(model));
    }
}
