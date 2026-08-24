using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Features.CompleteProbationReview;

namespace HR.Modules.Probation.Tests;

public class CompleteProbationReviewValidatorTests
{
    private readonly CompleteProbationReviewValidator _validator = new();

    [Fact]
    public async Task ValidRequest_Without_Outcome_Passes()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            Notes = "Good progress."
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidRequest_With_Pass_Outcome_Passes()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            Outcome = ProbationOutcome.Pass,
            DecisionDate = new DateOnly(2026, 9, 1)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidRequest_With_Extend_Outcome_Passes()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            Outcome = ProbationOutcome.Extend,
            DecisionDate = new DateOnly(2026, 9, 1),
            NewExpectedEndDate = new DateOnly(2026, 12, 1),
            ExtensionReason = "Needs more time to meet targets."
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Outcome_Without_DecisionDate_Fails()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            Outcome = ProbationOutcome.Pass
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteProbationReviewRequest.DecisionDate));
    }

    [Fact]
    public async Task Extend_Outcome_Without_NewExpectedEndDate_Fails()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            Outcome = ProbationOutcome.Extend,
            DecisionDate = new DateOnly(2026, 9, 1),
            ExtensionReason = "Needs more time."
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteProbationReviewRequest.NewExpectedEndDate));
    }

    [Fact]
    public async Task Extend_Outcome_Without_ExtensionReason_Fails()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            Outcome = ProbationOutcome.Extend,
            DecisionDate = new DateOnly(2026, 9, 1),
            NewExpectedEndDate = new DateOnly(2026, 12, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteProbationReviewRequest.ExtensionReason));
    }

    [Fact]
    public async Task Extend_Outcome_With_NewExpectedEndDate_Today_Fails()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            Outcome = ProbationOutcome.Extend,
            DecisionDate = today,
            NewExpectedEndDate = today,
            ExtensionReason = "Needs more time to meet targets."
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteProbationReviewRequest.NewExpectedEndDate));
    }

    [Fact]
    public async Task Extend_Outcome_With_NewExpectedEndDate_Tomorrow_Passes()
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            Outcome = ProbationOutcome.Extend,
            DecisionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            NewExpectedEndDate = tomorrow,
            ExtensionReason = "Needs more time to meet targets."
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Extend_Outcome_With_Whitespace_ExtensionReason_Fails()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            Outcome = ProbationOutcome.Extend,
            DecisionDate = new DateOnly(2026, 9, 1),
            NewExpectedEndDate = new DateOnly(2026, 12, 1),
            ExtensionReason = "   "
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteProbationReviewRequest.ExtensionReason));
    }

    [Fact]
    public async Task Fail_Outcome_Without_NewExpectedEndDate_Passes()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            Outcome = ProbationOutcome.Fail,
            DecisionDate = new DateOnly(2026, 9, 1)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ExtensionReason_Exceeding_MaxLength_Fails()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            Outcome = ProbationOutcome.Extend,
            DecisionDate = new DateOnly(2026, 9, 1),
            NewExpectedEndDate = new DateOnly(2026, 12, 1),
            ExtensionReason = new string('x', 1001)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteProbationReviewRequest.ExtensionReason));
    }

    [Fact]
    public async Task Empty_CompanyId_Fails()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.Empty,
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteProbationReviewRequest.CompanyId));
    }

    [Fact]
    public async Task Empty_ProbationRecordId_Fails()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.Empty,
            ReviewId = Guid.NewGuid(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteProbationReviewRequest.ProbationRecordId));
    }

    [Fact]
    public async Task Empty_ReviewId_Fails()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.Empty,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteProbationReviewRequest.ReviewId));
    }

}
