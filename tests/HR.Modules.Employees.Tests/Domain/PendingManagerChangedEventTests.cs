using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Tests.Domain;

// Reliability fix: durable record of a manager-changed integration event that must survive an
// interruption between the report's ManagerId reassignment save and the publish loop in
// EmployeeDepartureFinalizer.CascadeManagerDepartureAsync. See that class's remarks for the full
// rationale.
public class PendingManagerChangedEventTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CreatedAt = OccurredAt.AddSeconds(1);

    [Fact]
    public void Create_Captures_Previous_And_New_Manager_And_Leaves_PublishedAt_Null()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var reportEmployeeId = Guid.NewGuid();
        var previousManagerId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        var leavingProcessId = Guid.NewGuid();

        var pendingEvent = PendingManagerChangedEvent.Create(
            id, companyId, reportEmployeeId, previousManagerId, newManagerId, leavingProcessId, OccurredAt, CreatedAt);

        Assert.Equal(id, pendingEvent.Id);
        Assert.Equal(companyId, pendingEvent.CompanyId);
        Assert.Equal(reportEmployeeId, pendingEvent.ReportEmployeeId);
        Assert.Equal(previousManagerId, pendingEvent.PreviousManagerId);
        Assert.Equal(newManagerId, pendingEvent.NewManagerId);
        Assert.Equal(leavingProcessId, pendingEvent.LeavingProcessId);
        Assert.Equal(OccurredAt, pendingEvent.OccurredAt);
        Assert.Equal(CreatedAt, pendingEvent.CreatedAt);
        Assert.Null(pendingEvent.PublishedAt);
    }

    [Fact]
    public void Create_Allows_Null_NewManagerId_When_No_Replacement_Manager_Was_Nominated()
    {
        var pendingEvent = PendingManagerChangedEvent.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), OccurredAt, CreatedAt);

        Assert.Null(pendingEvent.NewManagerId);
    }

    [Fact]
    public void MarkPublished_Sets_PublishedAt()
    {
        var pendingEvent = PendingManagerChangedEvent.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), OccurredAt, CreatedAt);
        var publishedAt = CreatedAt.AddMinutes(1);

        pendingEvent.MarkPublished(publishedAt);

        Assert.Equal(publishedAt, pendingEvent.PublishedAt);
    }

    [Fact]
    public void MarkPublished_Called_Twice_Is_Idempotent_And_Keeps_First_PublishedAt()
    {
        var pendingEvent = PendingManagerChangedEvent.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), OccurredAt, CreatedAt);
        var firstPublishedAt = CreatedAt.AddMinutes(1);
        var secondPublishedAt = CreatedAt.AddMinutes(5);

        pendingEvent.MarkPublished(firstPublishedAt);
        pendingEvent.MarkPublished(secondPublishedAt);

        Assert.Equal(firstPublishedAt, pendingEvent.PublishedAt);
    }
}
