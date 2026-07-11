using HR.Modules.Employees.Features.CreateEmployee;

namespace HR.Modules.Employees.Tests;

public class CreateEmployeeValidatorTests
{
    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.Empty,
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_FirstName_Is_Empty()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = string.Empty,
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.FirstName));
    }

    [Fact]
    public void Validate_Fails_When_LastName_Is_Empty()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = string.Empty,
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.LastName));
    }

    [Fact]
    public void Validate_Fails_When_WorkEmail_Is_Empty()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = string.Empty,
            StartDate = new DateOnly(2026, 7, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.WorkEmail));
    }

    [Fact]
    public void Validate_Fails_When_WorkEmail_Is_Invalid()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "not-an-email",
            StartDate = new DateOnly(2026, 7, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.WorkEmail));
    }

    [Fact]
    public void Validate_Fails_When_PersonalEmail_Is_Invalid()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            PersonalEmail = "not-an-email",
            StartDate = new DateOnly(2026, 7, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.PersonalEmail));
    }

    [Fact]
    public void Validate_Fails_When_DateOfBirth_Is_Empty()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = default,
            Nationality = "British",
            Gender = "Female"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.DateOfBirth));
    }

    [Fact]
    public void Validate_Fails_When_Nationality_Is_Empty()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = string.Empty,
            Gender = "Female"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.Nationality));
    }

    [Fact]
    public void Validate_Fails_When_Gender_Is_Empty()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = string.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.Gender));
    }

    [Fact]
    public void Validate_Passes_For_Valid_Minimal_Request()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            PositionProfileId = Guid.NewGuid(),
            EmploymentTypeId = Guid.NewGuid(),
            EmployeeNumber = "EMP-0001",
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = "Female"
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Valid_Full_Request()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            PositionProfileId = Guid.NewGuid(),
            EmploymentTypeId = Guid.NewGuid(),
            EmployeeNumber = "EMP-0001",
            ManagerId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            PreferredName = "Al",
            WorkEmail = "alice@example.com",
            PersonalEmail = "alice.personal@gmail.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = "Female"
        });

        Assert.True(result.IsValid);
    }
}
