using HR.Infrastructure.BackgroundJobs;
using HR.SharedKernel;

namespace HR.Infrastructure.Tests;

/// <summary>
/// OBT-REM-11: a background-job failure audit row must never carry the raw exception message —
/// not even after regex scrubbing — and must be deterministically keyed on the Hangfire job id so
/// a retried job (or a duplicate OnPerformed callback for the same attempt) yields exactly one row.
/// Only a fixed, closed-set failure category derived from the exception's .NET type is persisted.
/// </summary>
public class BackgroundJobFailedAuditEventTests
{
    private static readonly DateTimeOffset When = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static IAuditEvent Build(
        string jobId = "job-1",
        Exception? exception = null,
        Guid? companyId = null)
        => new BackgroundJobFailedAuditEvent(
            jobId,
            jobType: "SicknessEvidenceReminderJob",
            methodName: "ExecuteAsync",
            exception: exception ?? new InvalidOperationException("boom"),
            occurredAt: When,
            companyId: companyId);

    private static object Meta(IAuditEvent e) => e.Metadata!;

    private static string? MetaString(IAuditEvent e, string prop)
        => Meta(e).GetType().GetProperty(prop)!.GetValue(Meta(e)) as string;

    [Theory]
    [InlineData("Login failed for jane.doe@example.com from 10.0.0.1")]
    [InlineData("NI number QQ123456C rejected")]
    [InlineData("Patient diagnosis: chronic fatigue syndrome, see /uploads/employee-42/fitnote.pdf")]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9.abc.def rejected for user John Smith")]
    public void Raw_exception_message_is_never_persisted_regardless_of_content(string raw)
    {
        var e = Build(exception: new InvalidOperationException(raw));

        Assert.DoesNotContain(raw, e.Summary);
        var metadataJson = System.Text.Json.JsonSerializer.Serialize(Meta(e));
        Assert.DoesNotContain(raw, metadataJson);
        // The message text (or any scrubbed derivative of it) is nowhere in the metadata at all —
        // only the fixed category/exception-type fields are present.
        Assert.Equal(nameof(InvalidOperationException), MetaString(e, "ExceptionType"));
        Assert.Equal("ValidationOrDataIntegrityError", MetaString(e, "FailureCategory"));
    }

    [Theory]
    [InlineData(typeof(TimeoutException), "Timeout")]
    [InlineData(typeof(System.Net.Http.HttpRequestException), "ExternalServiceError")]
    [InlineData(typeof(KeyNotFoundException), "NotFound")]
    [InlineData(typeof(ArgumentException), "ValidationOrDataIntegrityError")]
    [InlineData(typeof(InvalidOperationException), "ValidationOrDataIntegrityError")]
    public void Exception_type_maps_to_a_fixed_safe_category(Type exceptionType, string expectedCategory)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "some message with PII: jane@example.com")!;
        var e = Build(exception: exception);

        Assert.Equal(expectedCategory, MetaString(e, "FailureCategory"));
    }

    [Fact]
    public void Unrecognised_exception_type_maps_to_unknown_category()
    {
        var e = Build(exception: new NotSupportedException("boom"));
        Assert.Equal("Unknown", MetaString(e, "FailureCategory"));
    }

    [Fact]
    public void Tenant_scoped_when_companyId_supplied()
    {
        var company = Guid.NewGuid();
        var e = Build(companyId: company);

        Assert.Equal(company, e.CompanyId);
        Assert.Equal("tenant", MetaString(e, "Scope"));
    }

    [Fact]
    public void System_wide_when_companyId_null()
    {
        var e = Build(companyId: null);

        Assert.Equal(Guid.Empty, e.CompanyId);
        Assert.Equal("system-wide", MetaString(e, "Scope"));
    }

    [Fact]
    public void System_wide_when_companyId_is_empty_guid()
    {
        var e = Build(companyId: Guid.Empty);

        Assert.Equal(Guid.Empty, e.CompanyId);
        Assert.Equal("system-wide", MetaString(e, "Scope"));
    }

    [Fact]
    public void EventId_is_deterministic_per_jobId()
    {
        Assert.Equal(Build(jobId: "abc").EventId, Build(jobId: "abc").EventId);
        Assert.NotEqual(Build(jobId: "abc").EventId, Build(jobId: "xyz").EventId);
    }

    [Fact]
    public void EntityId_matches_EventId()
    {
        var e = Build(jobId: "abc");
        Assert.Equal(e.EventId, e.EntityId);
    }

    [Fact]
    public void ActorType_is_ScheduledJob()
        => Assert.Equal(AuditActorType.ScheduledJob, Build().ActorType);

    [Fact]
    public void Metadata_carries_safe_structured_fields()
    {
        var e = Build(jobId: "job-42");
        Assert.Equal("job-42", MetaString(e, "HangfireJobId"));
        Assert.Equal("SicknessEvidenceReminderJob", MetaString(e, "JobType"));
        Assert.Equal("ExecuteAsync", MetaString(e, "MethodName"));
        Assert.Equal(nameof(InvalidOperationException), MetaString(e, "ExceptionType"));
    }
}
