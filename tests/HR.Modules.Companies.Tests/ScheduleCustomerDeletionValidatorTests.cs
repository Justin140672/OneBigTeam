using HR.Modules.Companies.Features.ScheduleCustomerDeletion;

namespace HR.Modules.Companies.Tests;

public class ScheduleCustomerDeletionValidatorTests
{
    private static ScheduleCustomerDeletionRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        Reason = "Customer requested account closure and data deletion.",
        CountdownDays = 30,
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = new ScheduleCustomerDeletionValidator().Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_CountdownDays_Is_Null()
    {
        var result = new ScheduleCustomerDeletionValidator().Validate(ValidRequest() with { CountdownDays = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = new ScheduleCustomerDeletionValidator().Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ScheduleCustomerDeletionRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Empty()
    {
        var result = new ScheduleCustomerDeletionValidator().Validate(ValidRequest() with { Reason = string.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ScheduleCustomerDeletionRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Is_Too_Short()
    {
        var result = new ScheduleCustomerDeletionValidator().Validate(ValidRequest() with { Reason = "abcd" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ScheduleCustomerDeletionRequest.Reason));
    }

    [Fact]
    public void Validate_Fails_When_Reason_Exceeds_1000_Characters()
    {
        var result = new ScheduleCustomerDeletionValidator().Validate(ValidRequest() with { Reason = new string('A', 1001) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ScheduleCustomerDeletionRequest.Reason));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public void Validate_Fails_When_CountdownDays_Is_Out_Of_Range(int countdownDays)
    {
        var result = new ScheduleCustomerDeletionValidator().Validate(ValidRequest() with { CountdownDays = countdownDays });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ScheduleCustomerDeletionRequest.CountdownDays));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(365)]
    public void Validate_Passes_When_CountdownDays_Is_At_Boundary(int countdownDays)
    {
        var result = new ScheduleCustomerDeletionValidator().Validate(ValidRequest() with { CountdownDays = countdownDays });
        Assert.True(result.IsValid);
    }
}
