using HR.Modules.Employees.Features.CompleteInitialEmployeeSetup;

namespace HR.Modules.Employees.Tests;

public class CompleteInitialEmployeeSetupValidatorTests
{
    private static CompleteInitialEmployeeSetupRequest ValidRequest() => new()
    {
        FirstName = "Alice",
        LastName = "Smith",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Nationality = "British",
        Gender = "Female",
        AddressLine1 = "1 Test Street",
        City = "London",
        PostCode = "SW1A 1AA"
    };

    [Fact]
    public void Validate_Passes_For_Valid_Minimal_Request()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Full_Valid_Request()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(new CompleteInitialEmployeeSetupRequest
        {
            FirstName = "Alice",
            LastName = "Smith",
            PreferredName = "Ally",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Nationality = "British",
            Gender = "Female",
            GenderOther = null,
            PersonalEmail = "alice.personal@example.com",
            PhoneNumber = "07700 900001",
            HomePhone = "01234 567890",
            AddressLine1 = "42 Acacia Avenue",
            AddressLine2 = "Flat 3",
            City = "Manchester",
            County = "Greater Manchester",
            PostCode = "M1 1AA",
            Country = "United Kingdom"
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_FirstName_Is_Empty()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { FirstName = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.FirstName));
    }

    [Fact]
    public void Validate_Fails_When_FirstName_Is_Whitespace()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { FirstName = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.FirstName));
    }

    [Fact]
    public void Validate_Fails_When_FirstName_Exceeds_100_Characters()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { FirstName = new string('A', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.FirstName));
    }

    [Fact]
    public void Validate_Passes_When_FirstName_Is_Exactly_100_Characters()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { FirstName = new string('A', 100) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_LastName_Is_Empty()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { LastName = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.LastName));
    }

    [Fact]
    public void Validate_Fails_When_LastName_Is_Whitespace()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { LastName = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.LastName));
    }

    [Fact]
    public void Validate_Fails_When_LastName_Exceeds_100_Characters()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { LastName = new string('B', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.LastName));
    }

    [Fact]
    public void Validate_Passes_When_LastName_Is_Exactly_100_Characters()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { LastName = new string('B', 100) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_DateOfBirth_Equals_1900_01_01()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { DateOfBirth = new DateOnly(1900, 1, 1) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.DateOfBirth));
    }

    [Fact]
    public void Validate_Fails_When_DateOfBirth_Is_Before_1900_01_01()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { DateOfBirth = new DateOnly(1899, 12, 31) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.DateOfBirth));
    }

    [Fact]
    public void Validate_Fails_When_DateOfBirth_Is_Default()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { DateOfBirth = default });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.DateOfBirth));
    }

    [Fact]
    public void Validate_Passes_When_DateOfBirth_Is_One_Day_After_1900_01_01()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { DateOfBirth = new DateOnly(1900, 1, 2) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_Nationality_Is_Empty()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { Nationality = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.Nationality));
    }

    [Fact]
    public void Validate_Fails_When_Nationality_Is_Whitespace()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { Nationality = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.Nationality));
    }

    [Fact]
    public void Validate_Fails_When_Gender_Is_Empty()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { Gender = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.Gender));
    }

    [Fact]
    public void Validate_Fails_When_Gender_Is_Whitespace()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { Gender = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.Gender));
    }

    [Fact]
    public void Validate_Fails_When_PersonalEmail_Is_Invalid()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { PersonalEmail = "not-an-email" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.PersonalEmail));
    }

    [Fact]
    public void Validate_Passes_When_PersonalEmail_Is_Null()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { PersonalEmail = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_PersonalEmail_Is_Empty()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { PersonalEmail = string.Empty });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_PersonalEmail_Is_Whitespace()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { PersonalEmail = "   " });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_PersonalEmail_Is_Valid()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { PersonalEmail = "alice.personal@example.com" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_AddressLine1_Is_Empty()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { AddressLine1 = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.AddressLine1));
    }

    [Fact]
    public void Validate_Fails_When_AddressLine1_Is_Whitespace()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { AddressLine1 = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.AddressLine1));
    }

    [Fact]
    public void Validate_Fails_When_City_Is_Empty()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { City = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.City));
    }

    [Fact]
    public void Validate_Fails_When_City_Is_Whitespace()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { City = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.City));
    }

    [Fact]
    public void Validate_Fails_When_PostCode_Is_Empty()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { PostCode = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.PostCode));
    }

    [Fact]
    public void Validate_Fails_When_PostCode_Is_Whitespace()
    {
        var v = new CompleteInitialEmployeeSetupValidator();
        var result = v.Validate(ValidRequest() with { PostCode = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteInitialEmployeeSetupRequest.PostCode));
    }
}
