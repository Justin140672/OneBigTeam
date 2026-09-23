using System.Text.Json;
using HR.Modules.Identity.Services.AccountEmailPolicy;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Identity.Tests.AccountEmailPolicy;

/// <summary>
/// Ticket 9: the guard every account-creation handler calls before persisting anything. Covers the
/// exact failure contract (code + message), the single privacy-safe audit event (domain only, never
/// the address), actor attribution, and the no-op paths.
/// </summary>
public class AccountCreationEmailGuardTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
    private const string LocalPart = "janet.unique-localpart";

    private static (AccountCreationEmailGuard Guard, FakeAuditEventPublisher Audit, ListLogger<AccountCreationEmailGuard> Logger) Build()
    {
        var audit = new FakeAuditEventPublisher();
        var logger = new ListLogger<AccountCreationEmailGuard>();
        var guard = new AccountCreationEmailGuard(AccountEmailDomainPolicy.Default, audit, new FakeClock(Now), logger);
        return (guard, audit, logger);
    }

    private static string Serialise(object? value) => JsonSerializer.Serialize(value);

    // ── Contract constants ──────────────────────────────────────────────────────

    [Fact]
    public void Constants_Match_The_Published_Contract()
    {
        Assert.Equal("work_email_required", AccountCreationEmailGuard.WorkEmailRequiredCode);
        Assert.Equal(
            "Please use your organisation's work email address. Public email services such as Gmail, Hotmail and Outlook.com cannot be used to create an account.",
            AccountCreationEmailGuard.WorkEmailRequiredMessage);
        Assert.Equal("PublicEmailDomain", AccountCreationEmailGuard.BulkExclusionReason);
    }

    // ── Blocked ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("janet.unique-localpart@gmail.com", "gmail.com")]
    [InlineData("janet.unique-localpart@GMAIL.COM", "gmail.com")]
    [InlineData("janet.unique-localpart@hotmail.co.uk", "hotmail.co.uk")]
    [InlineData("janet.unique-localpart@mx.mailinator.com", "mx.mailinator.com")]
    public async Task EnsureAllowedAsync_Blocked_Address_Returns_WorkEmailRequired_And_Publishes_One_Audit_Event(
        string email, string expectedDomain)
    {
        var (guard, audit, _) = Build();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var result = await guard.EnsureAllowedAsync(
            email, AccountCreationPath.EmployeeInvitation, companyId, employeeId, actorUserId: Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("work_email_required", result.Error.Code);
        Assert.Equal(AccountCreationEmailGuard.WorkEmailRequiredMessage, result.Error.Message);

        var evt = Assert.IsType<AccountCreationEmailRejectedAuditEvent>(Assert.Single(audit.PublishedEvents));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal("employee-invitation", evt.Path);
        Assert.Equal(new[] { expectedDomain }, evt.Domains);
        Assert.Equal(new[] { employeeId }, evt.SubjectEmployeeIds);
        Assert.Equal(1, evt.RejectedCount);
        Assert.Equal(employeeId, evt.EntityId);
        Assert.Equal(new DateTimeOffset(Now, TimeSpan.Zero), evt.OccurredAt);

        IAuditEvent auditEvent = evt;
        Assert.Equal("account-creation.email-domain-rejected", auditEvent.EventType);
        Assert.Equal(employeeId, auditEvent.EmployeeId);
    }

    [Fact]
    public async Task Audit_Event_Summary_And_Metadata_Never_Contain_The_Address_Or_Local_Part()
    {
        var (guard, audit, _) = Build();

        await guard.EnsureAllowedAsync(
            $"{LocalPart}@gmail.com", AccountCreationPath.PublicSignup, Guid.Empty, null, null, CancellationToken.None);

        IAuditEvent evt = Assert.IsType<AccountCreationEmailRejectedAuditEvent>(Assert.Single(audit.PublishedEvents));

        var summary = evt.Summary ?? string.Empty;
        var metadata = Serialise(evt.Metadata);
        var before = Serialise(evt.Before);
        var after = Serialise(evt.After);

        foreach (var text in new[] { summary, metadata, before, after })
        {
            Assert.DoesNotContain("@", text);
            Assert.DoesNotContain(LocalPart, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("gmail.com", metadata);
        Assert.Contains("public-signup", metadata);
        Assert.Contains("public-signup", summary);
    }

    [Fact]
    public async Task Rejection_Log_Contains_The_Domain_But_Never_The_Address()
    {
        var (guard, _, logger) = Build();

        await guard.EnsureAllowedAsync(
            $"{LocalPart}@gmail.com", AccountCreationPath.PublicSignup, Guid.Empty, null, null, CancellationToken.None);

        Assert.NotEmpty(logger.Messages);
        Assert.Contains("gmail.com", logger.Text);
        Assert.DoesNotContain("@", logger.Text);
        Assert.DoesNotContain(LocalPart, logger.Text, StringComparison.OrdinalIgnoreCase);
    }

    // ── Actor attribution ───────────────────────────────────────────────────────

    [Fact]
    public async Task Audit_ActorType_Is_Anonymous_When_No_Actor()
    {
        var (guard, audit, _) = Build();

        await guard.EnsureAllowedAsync(
            "person@gmail.com", AccountCreationPath.PublicSignup, Guid.Empty, null, actorUserId: null, CancellationToken.None);

        IAuditEvent evt = Assert.IsType<AccountCreationEmailRejectedAuditEvent>(Assert.Single(audit.PublishedEvents));
        Assert.Equal(AuditActorType.Anonymous, evt.ActorType);
        Assert.Null(evt.ActorUserId);
    }

    [Fact]
    public async Task Audit_ActorType_Is_Human_When_Actor_Present()
    {
        var (guard, audit, _) = Build();
        var actorId = Guid.NewGuid();

        await guard.EnsureAllowedAsync(
            "person@gmail.com", AccountCreationPath.EmployeeInvitation, Guid.NewGuid(), Guid.NewGuid(), actorId, CancellationToken.None);

        IAuditEvent evt = Assert.IsType<AccountCreationEmailRejectedAuditEvent>(Assert.Single(audit.PublishedEvents));
        Assert.Equal(AuditActorType.Human, evt.ActorType);
        Assert.Equal(actorId, evt.ActorUserId);
    }

    [Fact]
    public async Task RecordRejectionsAsync_Explicit_ActorType_Overrides_The_Default_Even_With_An_Actor()
    {
        var (guard, audit, _) = Build();
        var actorId = Guid.NewGuid();

        await guard.RecordRejectionsAsync(
            AccountCreationPath.BulkEmployeeInvitation,
            Guid.NewGuid(),
            [(Guid.NewGuid(), guard.Evaluate("person@gmail.com"))],
            actorId,
            CancellationToken.None,
            AuditActorType.ScheduledJob);

        IAuditEvent evt = Assert.IsType<AccountCreationEmailRejectedAuditEvent>(Assert.Single(audit.PublishedEvents));
        Assert.Equal(AuditActorType.ScheduledJob, evt.ActorType);
    }

    // ── Allowed ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("person@acme.example")]
    [InlineData("person@brightsparks-consulting.co.uk")]
    [InlineData("person@olive.com")]
    public async Task EnsureAllowedAsync_Allowed_Address_Succeeds_Without_Audit_Or_Log(string email)
    {
        var (guard, audit, logger) = Build();

        var result = await guard.EnsureAllowedAsync(
            email, AccountCreationPath.PublicSignup, Guid.Empty, null, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(audit.PublishedEvents);
        Assert.Empty(logger.Messages);
    }

    // ── Malformed ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("person@localhost")]
    public async Task EnsureAllowedAsync_Malformed_Address_Returns_Validation_And_Audits_Malformed_Domain(string? email)
    {
        var (guard, audit, _) = Build();

        var result = await guard.EnsureAllowedAsync(
            email, AccountCreationPath.PublicSignup, Guid.Empty, null, null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal("Enter a valid email address.", result.Error.Message);

        var evt = Assert.IsType<AccountCreationEmailRejectedAuditEvent>(Assert.Single(audit.PublishedEvents));
        Assert.Equal(new[] { "(malformed)" }, evt.Domains);
    }

    [Fact]
    public void ErrorFor_Maps_Malformed_To_Validation_And_Blocked_To_WorkEmailRequired()
    {
        var (guard, _, _) = Build();

        Assert.Equal("validation", AccountCreationEmailGuard.ErrorFor(guard.Evaluate("no-at-sign")).Code);
        Assert.Equal("work_email_required", AccountCreationEmailGuard.ErrorFor(guard.Evaluate("person@gmail.com")).Code);
    }

    // ── RecordRejectionsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task RecordRejectionsAsync_With_No_Rejections_Publishes_Nothing_And_Logs_Nothing()
    {
        var (guard, audit, logger) = Build();

        await guard.RecordRejectionsAsync(
            AccountCreationPath.BulkEmployeeInvitation, Guid.NewGuid(), [], Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(audit.PublishedEvents);
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public async Task RecordRejectionsAsync_Many_Rejections_Produce_One_Event_With_Distinct_Sorted_Domains()
    {
        var (guard, audit, _) = Build();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var employeeC = Guid.NewGuid();

        await guard.RecordRejectionsAsync(
            AccountCreationPath.BulkEmployeeInvitation,
            Guid.NewGuid(),
            [
                (employeeA, guard.Evaluate("a@yahoo.com")),
                (employeeB, guard.Evaluate("b@gmail.com")),
                (employeeC, guard.Evaluate("c@gmail.com")),
                (employeeC, guard.Evaluate("c2@gmail.com")),
                (null, guard.Evaluate("not-an-email")),
            ],
            Guid.NewGuid(),
            CancellationToken.None);

        var evt = Assert.IsType<AccountCreationEmailRejectedAuditEvent>(Assert.Single(audit.PublishedEvents));
        Assert.Equal(5, evt.RejectedCount);
        Assert.Equal(new[] { "(malformed)", "gmail.com", "yahoo.com" }, evt.Domains);
        Assert.Equal(3, evt.SubjectEmployeeIds.Count);
        Assert.Contains(employeeA, evt.SubjectEmployeeIds);
        Assert.Contains(employeeB, evt.SubjectEmployeeIds);
        Assert.Contains(employeeC, evt.SubjectEmployeeIds);
        Assert.Equal("bulk-employee-invitation", evt.Path);

        IAuditEvent auditEvent = evt;
        // Multiple subjects: no single employee to attribute, and the summary reports the count.
        Assert.Null(auditEvent.EmployeeId);
        Assert.Contains("5 recipients", auditEvent.Summary);
        Assert.DoesNotContain("@", Serialise(auditEvent.Metadata));
    }

    [Fact]
    public async Task RecordRejectionsAsync_Without_Subject_Employee_Uses_A_Fresh_EntityId()
    {
        var (guard, audit, _) = Build();

        await guard.RecordRejectionsAsync(
            AccountCreationPath.PublicSignup, Guid.Empty, [(null, guard.Evaluate("person@gmail.com"))], null, CancellationToken.None);

        var evt = Assert.IsType<AccountCreationEmailRejectedAuditEvent>(Assert.Single(audit.PublishedEvents));
        Assert.NotEqual(Guid.Empty, evt.EntityId);
        Assert.Empty(evt.SubjectEmployeeIds);
    }

    [Theory]
    [InlineData(nameof(AccountCreationPath.PublicSignup), "public-signup")]
    [InlineData(nameof(AccountCreationPath.EmployeeInvitation), "employee-invitation")]
    [InlineData(nameof(AccountCreationPath.BulkEmployeeInvitation), "bulk-employee-invitation")]
    [InlineData(nameof(AccountCreationPath.InvitationAcceptance), "invitation-acceptance")]
    [InlineData(nameof(AccountCreationPath.PlatformAdministrator), "platform-administrator")]
    public void PathName_Maps_Every_Path(string pathName, string expected)
    {
        var path = Enum.Parse<AccountCreationPath>(pathName);

        Assert.Equal(expected, AccountCreationEmailGuard.PathName(path));
    }
}
