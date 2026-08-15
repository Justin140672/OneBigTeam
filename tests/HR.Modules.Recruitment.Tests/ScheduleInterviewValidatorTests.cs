using HR.Modules.Recruitment.Features.ScheduleInterview;

namespace HR.Modules.Recruitment.Tests;

public class ScheduleInterviewValidatorTests
{
    private readonly ScheduleInterviewValidator _validator = new();
    private static readonly DateTimeOffset ScheduledAt = new(2026, 7, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new ScheduleInterviewRequest
        {
            CompanyId             = Guid.NewGuid(),
            VacancyId             = Guid.NewGuid(),
            ApplicationId         = Guid.NewGuid(),
            InterviewerEmployeeId = Guid.NewGuid(),
            ScheduledAt           = ScheduledAt,
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_ApplicationId_Is_Empty()
    {
        var result = _validator.Validate(new ScheduleInterviewRequest
        {
            CompanyId             = Guid.NewGuid(),
            VacancyId             = Guid.NewGuid(),
            ApplicationId         = Guid.Empty,
            InterviewerEmployeeId = Guid.NewGuid(),
            ScheduledAt           = ScheduledAt,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ScheduleInterviewRequest.ApplicationId));
    }

    [Fact]
    public void Validate_Fails_When_ScheduledAt_Is_Default()
    {
        var result = _validator.Validate(new ScheduleInterviewRequest
        {
            CompanyId             = Guid.NewGuid(),
            VacancyId             = Guid.NewGuid(),
            ApplicationId         = Guid.NewGuid(),
            InterviewerEmployeeId = Guid.NewGuid(),
            ScheduledAt           = default,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ScheduleInterviewRequest.ScheduledAt));
    }

    [Fact]
    public void Validate_Fails_When_DurationMinutes_Is_Not_Positive()
    {
        var result = _validator.Validate(new ScheduleInterviewRequest
        {
            CompanyId             = Guid.NewGuid(),
            VacancyId             = Guid.NewGuid(),
            ApplicationId         = Guid.NewGuid(),
            InterviewerEmployeeId = Guid.NewGuid(),
            ScheduledAt           = ScheduledAt,
            DurationMinutes       = 0,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ScheduleInterviewRequest.DurationMinutes));
    }

    [Fact]
    public void Validate_Fails_When_Location_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new ScheduleInterviewRequest
        {
            CompanyId             = Guid.NewGuid(),
            VacancyId             = Guid.NewGuid(),
            ApplicationId         = Guid.NewGuid(),
            InterviewerEmployeeId = Guid.NewGuid(),
            ScheduledAt           = ScheduledAt,
            Location              = new string('A', 201),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ScheduleInterviewRequest.Location));
    }

    [Fact]
    public void Validate_Passes_When_Location_Is_Exactly_Max_Length()
    {
        var result = _validator.Validate(new ScheduleInterviewRequest
        {
            CompanyId             = Guid.NewGuid(),
            VacancyId             = Guid.NewGuid(),
            ApplicationId         = Guid.NewGuid(),
            InterviewerEmployeeId = Guid.NewGuid(),
            ScheduledAt           = ScheduledAt,
            Location              = new string('A', 200),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_DurationMinutes_Is_Negative()
    {
        var result = _validator.Validate(new ScheduleInterviewRequest
        {
            CompanyId             = Guid.NewGuid(),
            VacancyId             = Guid.NewGuid(),
            ApplicationId         = Guid.NewGuid(),
            InterviewerEmployeeId = Guid.NewGuid(),
            ScheduledAt           = ScheduledAt,
            DurationMinutes       = -1,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ScheduleInterviewRequest.DurationMinutes));
    }

    [Fact]
    public void Validate_Passes_When_DurationMinutes_Is_One()
    {
        // Boundary: GreaterThan(0) — 1 is the smallest valid value.
        var result = _validator.Validate(new ScheduleInterviewRequest
        {
            CompanyId             = Guid.NewGuid(),
            VacancyId             = Guid.NewGuid(),
            ApplicationId         = Guid.NewGuid(),
            InterviewerEmployeeId = Guid.NewGuid(),
            ScheduledAt           = ScheduledAt,
            DurationMinutes       = 1,
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_DurationMinutes_Is_Null()
    {
        var result = _validator.Validate(new ScheduleInterviewRequest
        {
            CompanyId             = Guid.NewGuid(),
            VacancyId             = Guid.NewGuid(),
            ApplicationId         = Guid.NewGuid(),
            InterviewerEmployeeId = Guid.NewGuid(),
            ScheduledAt           = ScheduledAt,
            DurationMinutes       = null,
        });

        Assert.True(result.IsValid);
    }
}
