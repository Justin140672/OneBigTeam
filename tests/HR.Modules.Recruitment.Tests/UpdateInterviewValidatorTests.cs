using HR.Modules.Recruitment.Features.UpdateInterview;

namespace HR.Modules.Recruitment.Tests;

public class UpdateInterviewValidatorTests
{
    private readonly UpdateInterviewValidator _validator = new();
    private static readonly DateTimeOffset ScheduledAt = new(2026, 7, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new UpdateInterviewRequest
        {
            CompanyId             = Guid.NewGuid(),
            VacancyId             = Guid.NewGuid(),
            ApplicationId         = Guid.NewGuid(),
            InterviewId           = Guid.NewGuid(),
            InterviewerEmployeeId = Guid.NewGuid(),
            ScheduledAt           = ScheduledAt,
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_InterviewId_Is_Empty()
    {
        var result = _validator.Validate(new UpdateInterviewRequest
        {
            CompanyId             = Guid.NewGuid(),
            VacancyId             = Guid.NewGuid(),
            ApplicationId         = Guid.NewGuid(),
            InterviewId           = Guid.Empty,
            InterviewerEmployeeId = Guid.NewGuid(),
            ScheduledAt           = ScheduledAt,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateInterviewRequest.InterviewId));
    }

    [Fact]
    public void Validate_Fails_When_DurationMinutes_Is_Not_Positive()
    {
        var result = _validator.Validate(new UpdateInterviewRequest
        {
            CompanyId             = Guid.NewGuid(),
            VacancyId             = Guid.NewGuid(),
            ApplicationId         = Guid.NewGuid(),
            InterviewId           = Guid.NewGuid(),
            InterviewerEmployeeId = Guid.NewGuid(),
            ScheduledAt           = ScheduledAt,
            DurationMinutes       = -5,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateInterviewRequest.DurationMinutes));
    }
}
