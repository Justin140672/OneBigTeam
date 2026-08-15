using HR.Modules.Companies.Features.GetAuditLog;

namespace HR.Modules.Companies.Tests;

public class GetAuditLogValidatorTests
{
    [Fact]
    public void Validate_Passes_For_Default_Request()
    {
        var result = new GetAuditLogValidator().Validate(new GetAuditLogRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_All_Filters_Are_Valid()
    {
        var result = new GetAuditLogValidator().Validate(new GetAuditLogRequest
        {
            CompanyId = Guid.NewGuid(),
            AdministratorEmail = "admin@example.com",
            FromDate = DateTimeOffset.UtcNow.AddDays(-7),
            ToDate = DateTimeOffset.UtcNow,
            EventType = AuditLogActionTypes.TrialExtended,
            PageNumber = 1,
            PageSize = 20,
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_AdministratorEmail_Exceeds_MaxLength()
    {
        var result = new GetAuditLogValidator().Validate(new GetAuditLogRequest
        {
            AdministratorEmail = new string('a', 321),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetAuditLogRequest.AdministratorEmail));
    }

    [Fact]
    public void Validate_Fails_When_EventType_Is_Not_A_Known_Action_Type()
    {
        var result = new GetAuditLogValidator().Validate(new GetAuditLogRequest
        {
            EventType = "not-a-real-event-type",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetAuditLogRequest.EventType));
    }

    [Theory]
    [MemberData(nameof(KnownEventTypes))]
    public void Validate_Passes_For_Every_Known_EventType(string eventType)
    {
        var result = new GetAuditLogValidator().Validate(new GetAuditLogRequest
        {
            EventType = eventType,
        });

        Assert.True(result.IsValid);
    }

    public static IEnumerable<object[]> KnownEventTypes() =>
        AuditLogActionTypes.All.Select(eventType => new object[] { eventType });

    [Fact]
    public void Validate_Fails_When_PageSize_Is_Zero()
    {
        var result = new GetAuditLogValidator().Validate(new GetAuditLogRequest { PageSize = 0 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetAuditLogRequest.PageSize));
    }

    [Fact]
    public void Validate_Fails_When_PageSize_Exceeds_Maximum()
    {
        var result = new GetAuditLogValidator().Validate(new GetAuditLogRequest { PageSize = 101 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetAuditLogRequest.PageSize));
    }

    [Fact]
    public void Validate_Passes_When_PageSize_Is_At_Maximum()
    {
        var result = new GetAuditLogValidator().Validate(new GetAuditLogRequest { PageSize = 100 });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_PageNumber_Is_Zero()
    {
        var result = new GetAuditLogValidator().Validate(new GetAuditLogRequest { PageNumber = 0 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetAuditLogRequest.PageNumber));
    }

    [Fact]
    public void Validate_Fails_When_FromDate_Is_After_ToDate()
    {
        var now = DateTimeOffset.UtcNow;

        var result = new GetAuditLogValidator().Validate(new GetAuditLogRequest
        {
            FromDate = now,
            ToDate = now.AddDays(-1),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetAuditLogRequest.FromDate));
    }

    [Fact]
    public void Validate_Passes_When_FromDate_Equals_ToDate()
    {
        var now = DateTimeOffset.UtcNow;

        var result = new GetAuditLogValidator().Validate(new GetAuditLogRequest
        {
            FromDate = now,
            ToDate = now,
        });

        Assert.True(result.IsValid);
    }
}
