using HR.Modules.Probation.Features.CreateProbationReview;

namespace HR.Modules.Probation.Tests;

public class CreateProbationReviewValidatorTests
{
    private readonly CreateProbationReviewValidator _validator = new();

    [Fact]
    public async Task ValidRequest_Passes()
    {
        var result = await _validator.ValidateAsync(new CreateProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewType = "ManagerCheckIn",
            DueDate = new DateOnly(2026, 7, 1)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task All_ReviewType_Values_Pass()
    {
        foreach (var type in new[] { "ManagerCheckIn", "HrReview", "FinalDecision", "ExtensionConfirmation" })
        {
            var result = await _validator.ValidateAsync(new CreateProbationReviewRequest
            {
                CompanyId = Guid.NewGuid(),
                ProbationRecordId = Guid.NewGuid(),
                ReviewType = type,
                DueDate = new DateOnly(2026, 7, 1)
            });

            Assert.True(result.IsValid, $"Expected {type} to be valid.");
        }
    }

    [Fact]
    public async Task Invalid_ReviewType_Fails()
    {
        var result = await _validator.ValidateAsync(new CreateProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.NewGuid(),
            ReviewType = "NotAType",
            DueDate = new DateOnly(2026, 7, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProbationReviewRequest.ReviewType));
    }

    [Fact]
    public async Task Empty_ProbationRecordId_Fails()
    {
        var result = await _validator.ValidateAsync(new CreateProbationReviewRequest
        {
            CompanyId = Guid.NewGuid(),
            ProbationRecordId = Guid.Empty,
            ReviewType = "ManagerCheckIn",
            DueDate = new DateOnly(2026, 7, 1)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProbationReviewRequest.ProbationRecordId));
    }
}
