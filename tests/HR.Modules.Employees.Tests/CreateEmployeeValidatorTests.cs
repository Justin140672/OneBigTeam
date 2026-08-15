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
    public void Validate_Fails_When_DepartmentId_Is_Empty()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.Empty,
            LocationId = Guid.NewGuid(),
            PositionProfileId = Guid.NewGuid(),
            EmploymentTypeId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = "Female"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.DepartmentId));
    }

    [Fact]
    public void Validate_Fails_When_LocationId_Is_Empty()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.Empty,
            PositionProfileId = Guid.NewGuid(),
            EmploymentTypeId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = "Female"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.LocationId));
    }

    [Fact]
    public void Validate_Fails_When_PositionProfileId_Is_Empty()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            PositionProfileId = Guid.Empty,
            EmploymentTypeId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = "Female"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.PositionProfileId));
    }

    [Fact]
    public void Validate_Fails_When_EmploymentTypeId_Is_Empty()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            PositionProfileId = Guid.NewGuid(),
            EmploymentTypeId = Guid.Empty,
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = "Female"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.EmploymentTypeId));
    }

    [Fact]
    public void Validate_Fails_When_StartDate_Is_Default()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            PositionProfileId = Guid.NewGuid(),
            EmploymentTypeId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = default,
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = "Female"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.StartDate));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Validate_Fails_When_FirstName_Is_Whitespace(string firstName)
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = firstName,
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = "Female"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.FirstName));
    }

    [Fact]
    public void Validate_Fails_When_FirstName_Exceeds_MaximumLength()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = new string('A', 101),
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = "Female"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.FirstName));
    }

    [Fact]
    public void Validate_Passes_When_FirstName_At_MaximumLength()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            PositionProfileId = Guid.NewGuid(),
            EmploymentTypeId = Guid.NewGuid(),
            FirstName = new string('A', 100),
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
    public void Validate_Fails_When_LastName_Exceeds_MaximumLength()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = new string('A', 101),
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = "Female"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.LastName));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Validate_Fails_When_LastName_Is_Whitespace(string lastName)
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = lastName,
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
    public void Validate_Fails_When_WorkEmail_Exceeds_MaximumLength()
    {
        var validator = new CreateEmployeeValidator();

        var localPart = new string('a', 310);
        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = $"{localPart}@example.com",
            StartDate = new DateOnly(2026, 7, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.WorkEmail));
    }

    [Fact]
    public void Validate_Fails_When_PersonalEmail_Exceeds_MaximumLength()
    {
        var validator = new CreateEmployeeValidator();

        var localPart = new string('a', 310);
        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            PersonalEmail = $"{localPart}@example.com",
            StartDate = new DateOnly(2026, 7, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.PersonalEmail));
    }

    [Fact]
    public void Validate_Fails_When_PreferredName_Exceeds_MaximumLength()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            PreferredName = new string('A', 101),
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.PreferredName));
    }

    [Fact]
    public void Validate_Passes_When_PreferredName_Is_Null()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            PositionProfileId = Guid.NewGuid(),
            EmploymentTypeId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            PreferredName = null,
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = "Female"
        });

        Assert.True(result.IsValid);
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
    public void Validate_Fails_When_Nationality_Exceeds_MaximumLength()
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
            Nationality = new string('A', 101),
            Gender = "Female"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.Nationality));
    }

    [Fact]
    public void Validate_Fails_When_Gender_Exceeds_MaximumLength()
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
            Gender = new string('A', 51)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.Gender));
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
    public void Validate_Passes_When_EmployeeNumber_Is_Empty()
    {
        // NotEmpty was intentionally removed: in Automatic numbering mode the request may omit
        // EmployeeNumber entirely, and requiredness in Manual mode is enforced by the handler
        // (which can read CompanySettings), not the validator.
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            PositionProfileId = Guid.NewGuid(),
            EmploymentTypeId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = "Female",
            EmployeeNumber = string.Empty
        });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("EMP-001")]
    [InlineData("EMP_001")]
    [InlineData("EMP.001")]
    [InlineData("EMP/001")]
    [InlineData("EMP 001")]
    [InlineData("emp001")]
    [InlineData("007")]
    public void Validate_Passes_For_Valid_EmployeeNumber_Formats(string employeeNumber)
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            PositionProfileId = Guid.NewGuid(),
            EmploymentTypeId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = "Female",
            EmployeeNumber = employeeNumber
        });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("EMP@001")]
    [InlineData("EMP#001")]
    [InlineData("EMP!001")]
    [InlineData("EMP*001")]
    public void Validate_Fails_For_Invalid_EmployeeNumber_Characters(string employeeNumber)
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
            Gender = "Female",
            EmployeeNumber = employeeNumber
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.EmployeeNumber));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeNumber_Exceeds_MaximumLength()
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
            Gender = "Female",
            EmployeeNumber = new string('A', 51)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.EmployeeNumber));
    }

    [Fact]
    public void Validate_Passes_When_EmployeeNumber_At_MaximumLength()
    {
        var validator = new CreateEmployeeValidator();

        var result = validator.Validate(new CreateEmployeeRequest
        {
            CompanyId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            PositionProfileId = Guid.NewGuid(),
            EmploymentTypeId = Guid.NewGuid(),
            FirstName = "Alice",
            LastName = "Smith",
            WorkEmail = "alice@example.com",
            StartDate = new DateOnly(2026, 7, 1),
            DateOfBirth = new DateOnly(1990, 5, 20),
            Nationality = "British",
            Gender = "Female",
            EmployeeNumber = new string('A', 50)
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
