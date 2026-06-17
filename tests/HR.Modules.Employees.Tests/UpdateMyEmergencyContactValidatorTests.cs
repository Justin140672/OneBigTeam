using HR.Modules.Employees.Features.UpdateMyEmergencyContact;

namespace HR.Modules.Employees.Tests;

public class UpdateMyEmergencyContactValidatorTests
{
    private static UpdateMyEmergencyContactRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        ContactId = Guid.NewGuid(),
        Name = "Jane Doe",
        Relationship = "Spouse",
        PhoneNumber = "07700 900000"
    };

    [Fact]
    public void Validate_Fails_When_Name_Is_Empty()
    {
        var v = new UpdateMyEmergencyContactValidator();
        var result = v.Validate(ValidRequest() with { Name = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyEmergencyContactRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Is_Whitespace()
    {
        var v = new UpdateMyEmergencyContactValidator();
        var result = v.Validate(ValidRequest() with { Name = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyEmergencyContactRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Name_Exceeds_200_Characters()
    {
        var v = new UpdateMyEmergencyContactValidator();
        var result = v.Validate(ValidRequest() with { Name = new string('A', 201) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyEmergencyContactRequest.Name));
    }

    [Fact]
    public void Validate_Fails_When_Relationship_Is_Empty()
    {
        var v = new UpdateMyEmergencyContactValidator();
        var result = v.Validate(ValidRequest() with { Relationship = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyEmergencyContactRequest.Relationship));
    }

    [Fact]
    public void Validate_Fails_When_Relationship_Exceeds_100_Characters()
    {
        var v = new UpdateMyEmergencyContactValidator();
        var result = v.Validate(ValidRequest() with { Relationship = new string('R', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyEmergencyContactRequest.Relationship));
    }

    [Fact]
    public void Validate_Fails_When_PhoneNumber_Is_Empty()
    {
        var v = new UpdateMyEmergencyContactValidator();
        var result = v.Validate(ValidRequest() with { PhoneNumber = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyEmergencyContactRequest.PhoneNumber));
    }

    [Fact]
    public void Validate_Fails_When_PhoneNumber_Exceeds_30_Characters()
    {
        var v = new UpdateMyEmergencyContactValidator();
        var result = v.Validate(ValidRequest() with { PhoneNumber = new string('0', 31) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyEmergencyContactRequest.PhoneNumber));
    }

    [Fact]
    public void Validate_Fails_When_Email_Is_Invalid()
    {
        var v = new UpdateMyEmergencyContactValidator();
        var result = v.Validate(ValidRequest() with { Email = "bad@" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateMyEmergencyContactRequest.Email));
    }

    [Fact]
    public void Validate_Passes_When_Email_Is_Null()
    {
        var v = new UpdateMyEmergencyContactValidator();
        var result = v.Validate(ValidRequest() with { Email = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Email_Is_Valid()
    {
        var v = new UpdateMyEmergencyContactValidator();
        var result = v.Validate(ValidRequest() with { Email = "jane@example.com" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Valid_Minimal_Request()
    {
        var v = new UpdateMyEmergencyContactValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Full_Valid_Request()
    {
        var v = new UpdateMyEmergencyContactValidator();
        var result = v.Validate(new UpdateMyEmergencyContactRequest
        {
            CompanyId = Guid.NewGuid(),
            ContactId = Guid.NewGuid(),
            Name = "Robert Smith",
            Relationship = "Father",
            PhoneNumber = "01234 999999",
            Email = "robert@example.com"
        });
        Assert.True(result.IsValid);
    }
}
