using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Tests;

public class EmployeeTimelineEntryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

    private static EmployeeTimelineEntry CreateEntry(
        Guid? id = null,
        Guid? companyId = null,
        Guid? employeeId = null,
        DateOnly? eventDate = null,
        EmployeeTimelineEventType eventType = EmployeeTimelineEventType.EmployeePromoted,
        EmployeeTimelineCategory category = EmployeeTimelineCategory.Employment,
        string title = "Employee promoted",
        string summary = "Employee was promoted.",
        Guid? performedByUserId = null,
        string sourceModule = "Employees",
        Guid? sourceRecordId = null,
        EmployeeTimelineVisibility visibility = EmployeeTimelineVisibility.AuthorisedInternal,
        DateTimeOffset? now = null) =>
        EmployeeTimelineEntry.Create(
            id ?? Guid.NewGuid(),
            companyId ?? Guid.NewGuid(),
            employeeId ?? Guid.NewGuid(),
            eventDate ?? new DateOnly(2026, 7, 20),
            eventType,
            category,
            title,
            summary,
            performedByUserId,
            sourceModule,
            sourceRecordId,
            visibility,
            now ?? FixedNow);

    [Fact]
    public void Create_Sets_All_Properties()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var eventDate = new DateOnly(2026, 7, 20);
        var performedByUserId = Guid.NewGuid();
        var sourceRecordId = Guid.NewGuid();

        var entry = EmployeeTimelineEntry.Create(
            id,
            companyId,
            employeeId,
            eventDate,
            EmployeeTimelineEventType.CompensationChanged,
            EmployeeTimelineCategory.Compensation,
            "Compensation changed",
            "Employee's compensation was changed.",
            performedByUserId,
            "Employees",
            sourceRecordId,
            EmployeeTimelineVisibility.HrOnly,
            FixedNow);

        Assert.Equal(id, entry.Id);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(eventDate, entry.EventDate);
        Assert.Equal(EmployeeTimelineEventType.CompensationChanged, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.Compensation, entry.Category);
        Assert.Equal("Compensation changed", entry.Title);
        Assert.Equal("Employee's compensation was changed.", entry.Summary);
        Assert.Equal(performedByUserId, entry.PerformedByUserId);
        Assert.Equal("Employees", entry.SourceModule);
        Assert.Equal(sourceRecordId, entry.SourceRecordId);
        Assert.Equal(EmployeeTimelineVisibility.HrOnly, entry.Visibility);
        Assert.Equal(FixedNow, entry.CreatedDate);
    }

    [Fact]
    public void Create_Allows_Null_PerformedByUserId_And_SourceRecordId()
    {
        var entry = CreateEntry(performedByUserId: null, sourceRecordId: null);

        Assert.Null(entry.PerformedByUserId);
        Assert.Null(entry.SourceRecordId);
    }

    [Fact]
    public void Entity_Has_No_Public_Setters()
    {
        var properties = typeof(EmployeeTimelineEntry).GetProperties();

        Assert.NotEmpty(properties);
        Assert.All(properties, p => Assert.True(p.SetMethod is null || !p.SetMethod.IsPublic));
    }

    [Fact]
    public void Entity_Has_No_Update_Method()
    {
        // Append-only log — Create is the only way to produce an instance, and there is
        // deliberately no method that mutates an existing entry after creation.
        var methods = typeof(EmployeeTimelineEntry).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        // Exclude property accessors (get_Xxx) which are compiler-generated methods, not
        // behaviour the entity exposes.
        var nonAccessorMethods = methods.Where(
            m => m.DeclaringType == typeof(EmployeeTimelineEntry) && !m.IsSpecialName);

        Assert.Empty(nonAccessorMethods);
    }
}
