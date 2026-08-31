using HR.Modules.Reporting.GovernanceReporting;

namespace HR.Modules.Reporting.Tests;

/// <summary>
/// ADM-08: pins the shared governance-audit query support — scope taxonomy, status derivation,
/// export column mapping and the "no sensitive columns" guarantee.
/// </summary>
public class GovernanceAuditReportSupportTests
{
    private static readonly string[] SensitiveTokens =
        ["salary", "national insurance", " ni ", "nino", "bank", "sort code", "token", "password"];

    // ── scope filtering ────────────────────────────────────────────────────

    [Fact]
    public void UserActivity_Scope_Matches_Any_Row_With_An_Actor()
    {
        Assert.True(GovernanceAuditReportSupport.MatchesScope(
            GovernanceAuditScope.UserActivity, "anything.happened", Guid.NewGuid()));
    }

    [Fact]
    public void UserActivity_Scope_Rejects_Rows_Without_An_Actor()
    {
        Assert.False(GovernanceAuditReportSupport.MatchesScope(
            GovernanceAuditScope.UserActivity, "anything.happened", null));
    }

    [Theory]
    [InlineData("company.settings-updated", true)]
    [InlineData("leave-policy.changed", true)]
    [InlineData("role.created", true)]
    [InlineData("employee.created", false)]
    [InlineData("login.succeeded", false)]
    public void AdministrativeChanges_Scope_Matches_Configuration_Prefixes_Only(string eventType, bool expected)
    {
        Assert.Equal(expected, GovernanceAuditReportSupport.MatchesScope(
            GovernanceAuditScope.AdministrativeChanges, eventType, Guid.NewGuid()));
    }

    [Theory]
    [InlineData("login.failed", true)]
    [InlineData("auth.mfa-challenged", true)]
    [InlineData("role.assigned", true)]
    [InlineData("session.expired", true)]
    [InlineData("employee.updated", false)]
    [InlineData("company.branding-updated", false)]
    public void SecurityEvents_Scope_Matches_Auth_And_AccountStatus_Prefixes_Only(string eventType, bool expected)
    {
        Assert.Equal(expected, GovernanceAuditReportSupport.MatchesScope(
            GovernanceAuditScope.SecurityEvents, eventType, Guid.NewGuid()));
    }

    [Fact]
    public void MatchesScope_Is_Case_Insensitive_On_EventType()
    {
        Assert.True(GovernanceAuditReportSupport.MatchesScope(
            GovernanceAuditScope.AdministrativeChanges, "COMPANY.SettingsUpdated", null));
    }

    // ── status derivation ──────────────────────────────────────────────────

    [Theory]
    [InlineData("login.failed", "Failed")]
    [InlineData("permission.denied", "Failed")]
    [InlineData("invite.rejected", "Failed")]
    [InlineData("export.error", "Failed")]
    [InlineData("login.succeeded", "Success")]
    [InlineData("company.updated", "Success")]
    public void DeriveStatus_Flags_Failure_Keywords(string eventType, string expected)
    {
        Assert.Equal(expected, GovernanceAuditReportSupport.DeriveStatus(eventType));
    }

    [Fact]
    public void DeriveStatus_Is_Case_Insensitive()
    {
        Assert.Equal("Failed", GovernanceAuditReportSupport.DeriveStatus("Login.FAILED"));
    }

    // ── export shape / sensitive data ──────────────────────────────────────

    [Fact]
    public void ColumnHeaders_Are_Exactly_The_Six_Known_Columns()
    {
        Assert.Equal(
            new[] { "Occurred At (UTC)", "Event Type", "Entity Type", "Actor", "Status", "Summary" },
            GovernanceAuditReportSupport.ColumnHeaders);
    }

    [Fact]
    public void ColumnHeaders_Contain_No_Sensitive_Tokens()
    {
        foreach (var header in GovernanceAuditReportSupport.ColumnHeaders)
        {
            var lower = header.ToLowerInvariant();
            Assert.DoesNotContain(SensitiveTokens, t => lower.Contains(t));
        }
    }

    [Fact]
    public void ToExportRow_Maps_The_Six_Columns_In_Order()
    {
        var occurredAt = new DateTimeOffset(2026, 8, 30, 14, 5, 9, TimeSpan.Zero);
        var row = new GovernanceAuditRow(
            occurredAt, "company.updated", "Company", Guid.NewGuid(), "admin@example.com",
            Guid.NewGuid(), "Success", "Changed the working week");

        var cells = GovernanceAuditReportSupport.ToExportRow(row);

        Assert.Equal(6, cells.Count);
        Assert.Equal("2026-08-30 14:05:09", cells[0]);
        Assert.Equal("company.updated", cells[1]);
        Assert.Equal("Company", cells[2]);
        Assert.Equal("admin@example.com", cells[3]);
        Assert.Equal("Success", cells[4]);
        Assert.Equal("Changed the working week", cells[5]);
    }

    [Fact]
    public void ToExportRow_Falls_Back_To_ActorUserId_Then_System_When_No_Email()
    {
        var actorId = Guid.NewGuid();
        var withId = new GovernanceAuditRow(
            DateTimeOffset.UtcNow, "e", "E", actorId, null, null, "Success", null);
        Assert.Equal(actorId.ToString(), GovernanceAuditReportSupport.ToExportRow(withId)[3]);

        var system = new GovernanceAuditRow(
            DateTimeOffset.UtcNow, "e", "E", null, null, null, "Success", null);
        Assert.Equal("System", GovernanceAuditReportSupport.ToExportRow(system)[3]);
    }

    // ── status filter helper ───────────────────────────────────────────────

    [Theory]
    [InlineData(null, true)]
    [InlineData("Success", true)]
    [InlineData("Failed", true)]
    [InlineData("failed", true)]
    [InlineData("Pending", false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    public void GovernanceReportFilters_IsValidStatus(string? value, bool expected)
    {
        Assert.Equal(expected, GovernanceReportFilters.IsValidStatus(value));
    }
}
