using HR.Infrastructure.BackgroundJobs;
using HR.SharedKernel;

namespace HR.Infrastructure.Tests;

/// <summary>
/// OBT-REM-02: a background-job failure audit row must never carry the raw exception message and
/// must be deterministically keyed on the Hangfire job id so a retried job yields exactly one row.
/// </summary>
public class BackgroundJobFailedAuditEventTests
{
    private static readonly DateTimeOffset When = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static IAuditEvent Build(
        string jobId = "job-1",
        string? message = null,
        Guid? companyId = null)
        => new BackgroundJobFailedAuditEvent(
            jobId,
            jobType: "SicknessEvidenceReminderJob",
            methodName: "ExecuteAsync",
            exception: new InvalidOperationException(message ?? "boom"),
            occurredAt: When,
            companyId: companyId);

    private static object Meta(IAuditEvent e) => e.Metadata!;

    private static string? MetaString(IAuditEvent e, string prop)
        => Meta(e).GetType().GetProperty(prop)!.GetValue(Meta(e)) as string;

    [Theory]
    [InlineData("Login failed for Bearer abc123.def456-ghi")]
    [InlineData("NI number QQ123456C rejected")]
    [InlineData("sort code 12-34-56 invalid")]
    [InlineData("account 1234567890123456 not found")]
    public void Sensitive_tokens_are_scrubbed_from_summary_and_metadata(string raw)
    {
        var e = Build(message: raw);

        Assert.Contains(SensitiveDataScrubber.Redacted, e.Summary);
        Assert.Contains(SensitiveDataScrubber.Redacted, MetaString(e, "ExceptionMessage")!);
        // the exact scrubbed form the scrubber would produce
        var scrubbed = SensitiveDataScrubber.ScrubText(raw);
        Assert.Contains(scrubbed, e.Summary);
        Assert.Equal(scrubbed, MetaString(e, "ExceptionMessage"));
    }

    [Fact]
    public void Clean_message_passes_through()
    {
        var e = Build(message: "database timeout after 30s");
        Assert.Contains("database timeout after 30s", e.Summary);
        Assert.Equal("database timeout after 30s", MetaString(e, "ExceptionMessage"));
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
