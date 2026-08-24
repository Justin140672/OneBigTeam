using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.CompleteReturnToWorkReview;

namespace HR.Modules.Sickness.Tests;

public class CompleteReturnToWorkReviewValidatorTests
{
    private readonly CompleteReturnToWorkReviewValidator _validator = new();

    private static CompleteReturnToWorkReviewRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        ReviewId = Guid.NewGuid(),
        Outcome = FitToReturnOutcome.Fit,
        AdjustmentsRequired = false,
        AdjustmentDetails = null,
        ManagerNotes = null
    };

    [Fact]
    public void Validates_Valid_Request()
    {
        var result = _validator.Validate(ValidRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "CompanyId");
    }

    [Fact]
    public void Fails_When_ReviewId_Is_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { ReviewId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ReviewId");
    }

    [Fact]
    public void Fails_When_Outcome_Is_Out_Of_Range()
    {
        var result = _validator.Validate(ValidRequest() with { Outcome = (FitToReturnOutcome)99 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Outcome");
    }

    // InlineData rows must not expose the internal FitToReturnOutcome enum directly (CS0051 —
    // a public [Theory] method's parameters must be at least as accessible as the method
    // itself, even between InternalsVisibleTo friend assemblies), so the underlying int value
    // is passed instead and cast back inside the test method.
    [Theory]
    [InlineData((int)FitToReturnOutcome.Fit)]
    [InlineData((int)FitToReturnOutcome.FitWithAdjustments)]
    [InlineData((int)FitToReturnOutcome.NotFit)]
    public void Succeeds_For_Each_Valid_Outcome_Value(int outcomeValue)
    {
        var outcome = (FitToReturnOutcome)outcomeValue;
        var result = _validator.Validate(ValidRequest() with { Outcome = outcome });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Fails_When_AdjustmentsRequired_True_And_AdjustmentDetails_Null()
    {
        var result = _validator.Validate(ValidRequest() with { AdjustmentsRequired = true, AdjustmentDetails = null });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "AdjustmentDetails");
    }

    [Fact]
    public void Fails_When_AdjustmentsRequired_True_And_AdjustmentDetails_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { AdjustmentsRequired = true, AdjustmentDetails = "" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "AdjustmentDetails");
    }

    [Fact]
    public void Fails_When_AdjustmentsRequired_True_And_AdjustmentDetails_Whitespace()
    {
        var result = _validator.Validate(ValidRequest() with { AdjustmentsRequired = true, AdjustmentDetails = "   " });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "AdjustmentDetails");
    }

    [Fact]
    public void Succeeds_When_AdjustmentsRequired_True_And_AdjustmentDetails_Provided()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            AdjustmentsRequired = true,
            AdjustmentDetails = "Phased return over two weeks."
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Succeeds_When_AdjustmentsRequired_False_And_AdjustmentDetails_Empty()
    {
        var result = _validator.Validate(ValidRequest() with { AdjustmentsRequired = false, AdjustmentDetails = "" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Succeeds_When_AdjustmentsRequired_False_And_AdjustmentDetails_Null()
    {
        var result = _validator.Validate(ValidRequest() with { AdjustmentsRequired = false, AdjustmentDetails = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Succeeds_When_AdjustmentDetails_Is_Exactly_MaximumLength()
    {
        var details = new string('a', 2000);
        var result = _validator.Validate(ValidRequest() with { AdjustmentsRequired = true, AdjustmentDetails = details });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Fails_When_AdjustmentDetails_Exceeds_MaximumLength()
    {
        var details = new string('a', 2001);
        var result = _validator.Validate(ValidRequest() with { AdjustmentsRequired = true, AdjustmentDetails = details });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "AdjustmentDetails");
    }

    [Fact]
    public void Succeeds_When_ManagerNotes_Is_Exactly_MaximumLength()
    {
        var notes = new string('a', 2000);
        var result = _validator.Validate(ValidRequest() with { ManagerNotes = notes });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Fails_When_ManagerNotes_Exceeds_MaximumLength()
    {
        var notes = new string('a', 2001);
        var result = _validator.Validate(ValidRequest() with { ManagerNotes = notes });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ManagerNotes");
    }

    [Fact]
    public void Succeeds_When_ManagerNotes_Is_Null()
    {
        var result = _validator.Validate(ValidRequest() with { ManagerNotes = null });
        Assert.True(result.IsValid);
    }
}
