using HR.Modules.Probation.Features.CompleteProbationReview;

namespace HR.Modules.Probation.Tests;

public class CompleteProbationReviewValidatorTests
{
    private readonly CompleteProbationReviewValidator _validator = new();

    [Fact]
    public async Task ValidRequest_Passes()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            CompletedByEmployeeId = Guid.NewGuid(),
            Notes = "Good progress."
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidRequest_Without_Notes_Passes()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            CompletedByEmployeeId = Guid.NewGuid(),
            Notes = null
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Empty_CompanyId_Fails()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.Empty,
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            CompletedByEmployeeId = Guid.NewGuid()
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
            CompletedByEmployeeId = Guid.NewGuid()
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
            CompletedByEmployeeId = Guid.NewGuid()
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteProbationReviewRequest.ReviewId));
    }

    [Fact]
    public async Task Empty_CompletedByEmployeeId_Fails()
    {
        var result = await _validator.ValidateAsync(new CompleteProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewId = Guid.NewGuid(),
            CompletedByEmployeeId = Guid.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CompleteProbationReviewRequest.CompletedByEmployeeId));
    }
}
