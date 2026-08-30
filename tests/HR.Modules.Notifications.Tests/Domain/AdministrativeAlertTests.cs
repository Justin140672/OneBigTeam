using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;

namespace HR.Modules.Notifications.Tests.Domain;

public class AdministrativeAlertTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static RaiseAdministrativeAlertCommand Command(
        AdministrativeAlertSeverity severity = AdministrativeAlertSeverity.Warning,
        DateTimeOffset? occurredAt = null) =>
        new(
            CompanyId: Guid.NewGuid(),
            Severity: severity,
            Category: AdministrativeAlertCategory.IntegrationDelivery,
            Summary: "Something failed",
            Detail: "detail",
            OccurredAt: occurredAt ?? Now,
            DedupKey: "dedup:1",
            AffectedEntityType: "EmailDelivery",
            AffectedEntityId: Guid.NewGuid(),
            RecommendedAction: "check config",
            ActionUrl: null);

    private static AdministrativeAlert Raise(
        AdministrativeAlertSeverity severity = AdministrativeAlertSeverity.Warning,
        DateTimeOffset? occurredAt = null) =>
        AdministrativeAlert.Raise(Guid.NewGuid(), Command(severity, occurredAt), Now);

    [Fact]
    public void Raise_Starts_Open_With_Single_Occurrence_And_Equal_First_And_Last_Timestamps()
    {
        var occurredAt = Now.AddMinutes(-5);
        var alert = Raise(occurredAt: occurredAt);

        Assert.Equal(AdministrativeAlertStatus.Open, alert.Status);
        Assert.Equal(1, alert.OccurrenceCount);
        Assert.Equal(occurredAt, alert.FirstOccurredAt);
        Assert.Equal(occurredAt, alert.LastOccurredAt);
        Assert.False(alert.IsRead);
    }

    [Fact]
    public void RecordRecurrence_Increments_Count_And_Advances_LastOccurredAt()
    {
        var alert = Raise(occurredAt: Now);
        var later = Now.AddHours(1);

        alert.RecordRecurrence(AdministrativeAlertSeverity.Warning, "again", "d", later);

        Assert.Equal(2, alert.OccurrenceCount);
        Assert.Equal(later, alert.LastOccurredAt);
        Assert.Equal(Now, alert.FirstOccurredAt);
    }

    [Fact]
    public void RecordRecurrence_Does_Not_Move_LastOccurredAt_Backwards_For_An_Older_Occurrence()
    {
        var alert = Raise(occurredAt: Now);

        alert.RecordRecurrence(AdministrativeAlertSeverity.Warning, "again", "d", Now.AddHours(-3));

        Assert.Equal(Now, alert.LastOccurredAt);
    }

    [Fact]
    public void RecordRecurrence_Raises_Severity_To_The_Max_But_Never_Lowers_It()
    {
        var alert = Raise(AdministrativeAlertSeverity.Warning);

        alert.RecordRecurrence(AdministrativeAlertSeverity.Critical, "s", null, Now);
        Assert.Equal(AdministrativeAlertSeverity.Critical, alert.Severity);

        alert.RecordRecurrence(AdministrativeAlertSeverity.Info, "s", null, Now);
        Assert.Equal(AdministrativeAlertSeverity.Critical, alert.Severity);
    }

    [Fact]
    public void RecordRecurrence_Clears_IsRead()
    {
        var alert = Raise();
        alert.MarkAsRead();
        Assert.True(alert.IsRead);

        alert.RecordRecurrence(AdministrativeAlertSeverity.Warning, "s", null, Now);

        Assert.False(alert.IsRead);
    }

    [Fact]
    public void RecordRecurrence_Reopens_An_Acknowledged_Alert()
    {
        var alert = Raise();
        alert.Acknowledge(Guid.NewGuid(), Now);
        Assert.Equal(AdministrativeAlertStatus.Acknowledged, alert.Status);

        alert.RecordRecurrence(AdministrativeAlertSeverity.Warning, "s", null, Now.AddMinutes(1));

        Assert.Equal(AdministrativeAlertStatus.Open, alert.Status);
    }

    [Fact]
    public void RecordRecurrence_Leaves_An_Open_Alert_Open()
    {
        var alert = Raise();

        alert.RecordRecurrence(AdministrativeAlertSeverity.Warning, "s", null, Now.AddMinutes(1));

        Assert.Equal(AdministrativeAlertStatus.Open, alert.Status);
    }

    [Fact]
    public void MarkAsRead_Sets_IsRead_True()
    {
        var alert = Raise();

        alert.MarkAsRead();

        Assert.True(alert.IsRead);
    }

    [Fact]
    public void Acknowledge_From_Open_Transitions_To_Acknowledged_And_Sets_Attribution()
    {
        var alert = Raise();
        var userId = Guid.NewGuid();
        var at = Now.AddMinutes(2);

        alert.Acknowledge(userId, at);

        Assert.Equal(AdministrativeAlertStatus.Acknowledged, alert.Status);
        Assert.Equal(at, alert.AcknowledgedAt);
        Assert.Equal(userId, alert.AcknowledgedByUserId);
        Assert.True(alert.IsRead);
    }

    [Fact]
    public void Acknowledge_When_Already_Acknowledged_Throws()
    {
        var alert = Raise();
        alert.Acknowledge(Guid.NewGuid(), Now);

        Assert.Throws<InvalidOperationException>(() => alert.Acknowledge(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Acknowledge_When_Resolved_Throws()
    {
        var alert = Raise();
        alert.Resolve(Guid.NewGuid(), null, Now);

        Assert.Throws<InvalidOperationException>(() => alert.Acknowledge(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Resolve_From_Open_Transitions_To_Resolved_And_Sets_Attribution()
    {
        var alert = Raise();
        var userId = Guid.NewGuid();
        var at = Now.AddMinutes(3);

        alert.Resolve(userId, "  fixed it  ", at);

        Assert.Equal(AdministrativeAlertStatus.Resolved, alert.Status);
        Assert.Equal(at, alert.ResolvedAt);
        Assert.Equal(userId, alert.ResolvedByUserId);
        Assert.Equal("fixed it", alert.ResolutionNote);
        Assert.True(alert.IsRead);
    }

    [Fact]
    public void Resolve_From_Acknowledged_Is_Allowed()
    {
        var alert = Raise();
        alert.Acknowledge(Guid.NewGuid(), Now);

        alert.Resolve(Guid.NewGuid(), null, Now.AddMinutes(1));

        Assert.Equal(AdministrativeAlertStatus.Resolved, alert.Status);
        Assert.Null(alert.ResolutionNote);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Resolve_Normalizes_Blank_Note_To_Null(string? note)
    {
        var alert = Raise();

        alert.Resolve(Guid.NewGuid(), note, Now);

        Assert.Null(alert.ResolutionNote);
    }

    [Fact]
    public void Resolve_When_Already_Resolved_Throws()
    {
        var alert = Raise();
        alert.Resolve(Guid.NewGuid(), null, Now);

        Assert.Throws<InvalidOperationException>(() => alert.Resolve(Guid.NewGuid(), null, Now));
    }
}
