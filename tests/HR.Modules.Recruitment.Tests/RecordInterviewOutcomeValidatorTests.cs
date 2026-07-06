using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RecordInterviewOutcome;

namespace HR.Modules.Recruitment.Tests;

public class RecordInterviewOutcomeValidatorTests
{
    private readonly RecordInterviewOutcomeValidator _validator = new();

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new RecordInterviewOutcomeRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            InterviewId   = Guid.NewGuid(),
            Outcome       = InterviewOutcome.Passed,
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_InterviewId_Is_Empty()
    {
        var result = _validator.Validate(new RecordInterviewOutcomeRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            InterviewId   = Guid.Empty,
            Outcome       = InterviewOutcome.Passed,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RecordInterviewOutcomeRequest.InterviewId));
    }

    [Fact]
    public void Validate_Fails_When_Outcome_Is_Pending()
    {
        var result = _validator.Validate(new RecordInterviewOutcomeRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            InterviewId   = Guid.NewGuid(),
            Outcome       = InterviewOutcome.Pending,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RecordInterviewOutcomeRequest.Outcome));
    }

    [Fact]
    public void Validate_Fails_When_Notes_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new RecordInterviewOutcomeRequest
        {
            CompanyId     = Guid.NewGuid(),
            VacancyId     = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            InterviewId   = Guid.NewGuid(),
            Outcome       = InterviewOutcome.Failed,
            Notes         = new string('A', 2001),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RecordInterviewOutcomeRequest.Notes));
    }
}
