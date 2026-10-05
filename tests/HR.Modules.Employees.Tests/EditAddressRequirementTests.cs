using HR.Modules.Employees.Features.UpdateEmployeeProfile;
using HR.Modules.Employees.Features.UpdateEmployeeProfileAndEmployment;
using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Tests;

public class EditAddressRequirementTests
{
    private static UpdateEmployeeProfileRequest Profile() => new()
    {
        CompanyId = Guid.NewGuid(), Id = Guid.NewGuid(), FirstName = "A", LastName = "B", WorkEmail = "a@example.com",
        StartDate = new DateOnly(2026, 1, 1), ExpectedVersion = 1,
        AddressLine1 = "1 High Street", City = "London", PostCode = "SW1A 1AA",
    };

    private static UpdateEmployeeProfileAndEmploymentRequest Combined() => new()
    {
        CompanyId = Guid.NewGuid(), Id = Guid.NewGuid(), FirstName = "A", LastName = "B", WorkEmail = "a@example.com",
        StartDate = new DateOnly(2026, 1, 1), ExpectedVersion = 1, EmployeeNumber = "EMP-1", Status = EmploymentStatus.Active,
        AddressLine1 = "1 High Street", City = "London", PostCode = "SW1A 1AA",
    };

    [Theory]
    [InlineData(nameof(UpdateEmployeeProfileRequest.AddressLine1), null)]
    [InlineData(nameof(UpdateEmployeeProfileRequest.AddressLine1), "   ")]
    [InlineData(nameof(UpdateEmployeeProfileRequest.City), "")]
    [InlineData(nameof(UpdateEmployeeProfileRequest.City), "  ")]
    [InlineData(nameof(UpdateEmployeeProfileRequest.PostCode), null)]
    [InlineData(nameof(UpdateEmployeeProfileRequest.PostCode), "  ")]
    public void Both_Validators_Reject_Blank_Required_Address_Fields(string field, string? value)
    {
        var p = field switch
        {
            nameof(UpdateEmployeeProfileRequest.AddressLine1) => Profile() with { AddressLine1 = value },
            nameof(UpdateEmployeeProfileRequest.City) => Profile() with { City = value },
            _ => Profile() with { PostCode = value },
        };
        var c = field switch
        {
            nameof(UpdateEmployeeProfileRequest.AddressLine1) => Combined() with { AddressLine1 = value },
            nameof(UpdateEmployeeProfileRequest.City) => Combined() with { City = value },
            _ => Combined() with { PostCode = value },
        };

        Assert.Contains(new UpdateEmployeeProfileValidator().Validate(p).Errors, e => e.PropertyName == field);
        Assert.Contains(new UpdateEmployeeProfileAndEmploymentValidator().Validate(c).Errors, e => e.PropertyName == field);
    }

    [Fact]
    public void Both_Validators_Accept_A_Complete_Address()
    {
        Assert.True(new UpdateEmployeeProfileValidator().Validate(Profile()).IsValid);
        Assert.True(new UpdateEmployeeProfileAndEmploymentValidator().Validate(Combined()).IsValid);
    }
}
