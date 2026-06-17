using HR.Modules.Leave.Features.AwardToil;

namespace HR.Modules.Leave.Tests;

public class AwardToilValidatorTests
{
    private static AwardToilRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        AwardedByEmployeeId = Guid.NewGuid(),
        Days = 1.0m,
        OccurredOn = new DateOnly(2026, 6, 1)
    };

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var v = new AwardToilValidator();
        var result = v.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AwardToilRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var v = new AwardToilValidator();
        var result = v.Validate(ValidRequest() with { EmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AwardToilRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_AwardedByEmployeeId_Is_Empty()
    {
        var v = new AwardToilValidator();
        var result = v.Validate(ValidRequest() with { AwardedByEmployeeId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AwardToilRequest.AwardedByEmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_Days_Is_Zero()
    {
        var v = new AwardToilValidator();
        var result = v.Validate(ValidRequest() with { Days = 0m });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AwardToilRequest.Days));
    }

    [Fact]
    public void Validate_Fails_When_Days_Is_Negative()
    {
        var v = new AwardToilValidator();
        var result = v.Validate(ValidRequest() with { Days = -0.5m });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AwardToilRequest.Days));
    }

    [Fact]
    public void Validate_Fails_When_OccurredOn_Is_Default()
    {
        var v = new AwardToilValidator();
        var result = v.Validate(ValidRequest() with { OccurredOn = default });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AwardToilRequest.OccurredOn));
    }

    [Fact]
    public void Validate_Fails_When_Notes_Exceeds_500_Characters()
    {
        var v = new AwardToilValidator();
        var result = v.Validate(ValidRequest() with { Notes = new string('N', 501) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AwardToilRequest.Notes));
    }

    [Fact]
    public void Validate_Passes_When_Notes_Is_Null()
    {
        var v = new AwardToilValidator();
        var result = v.Validate(ValidRequest() with { Notes = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Notes_Is_At_Max_Length()
    {
        var v = new AwardToilValidator();
        var result = v.Validate(ValidRequest() with { Notes = new string('N', 500) });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Fractional_Days()
    {
        var v = new AwardToilValidator();
        var result = v.Validate(ValidRequest() with { Days = 0.5m });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var v = new AwardToilValidator();
        Assert.True(v.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Passes_With_Notes()
    {
        var v = new AwardToilValidator();
        var result = v.Validate(ValidRequest() with { Notes = "Overtime compensation for Q2 sprint." });
        Assert.True(result.IsValid);
    }
}
