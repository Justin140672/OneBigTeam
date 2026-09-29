using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateTimelineEntryOnEmployeePromoted;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class EmployeePromotedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Writes_Timeline_Entry_With_Resolved_Position_Titles()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();

        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "HQ", null, now);
        context.Departments.Add(department);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);

        var previousPosition = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        var newPosition = PositionProfile.Create(Guid.NewGuid(), companyId, department.Id, location.Id, "Senior Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.AddRange(previousPosition, newPosition);
        await context.SaveChangesAsync();

        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new EmployeePromotedHandler(context, timelineWriter);

        var employeeId = Guid.NewGuid();
        var effectiveDate = new DateOnly(2026, 8, 1);
        var promotionId = Guid.NewGuid();

        await handler.HandleAsync(
            new EmployeePromotedIntegrationEvent(companyId, employeeId, previousPosition.Id, newPosition.Id, effectiveDate, promotionId),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(effectiveDate, entry.EventDate);
        Assert.Equal(EmployeeTimelineEventType.EmployeePromoted, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.Employment, entry.Category);
        Assert.Equal(EmployeeTimelineVisibility.AuthorisedInternal, entry.Visibility);
        Assert.Contains("Engineer", entry.Summary);
        Assert.Contains("Senior Engineer", entry.Summary);
        Assert.Equal(promotionId, entry.SourceRecordId);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Generic_Phrasing_When_Position_Profiles_Not_Found()
    {
        await using var context = BuildContext();
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new EmployeePromotedHandler(context, timelineWriter);

        await handler.HandleAsync(
            new EmployeePromotedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 1), Guid.NewGuid()),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Contains("their previous role", entry.Summary);
        Assert.Contains("a new role", entry.Summary);
    }

    // ---- Internal recruitment Ticket 7 ----

    [Theory]
    [InlineData("recruitment:application:5a0c1c1e-8d0f-4b5e-9f53-1f6c0a7e2b10", "Internal appointment")]
    [InlineData(null, "Promoted")]
    [InlineData("import:batch:42", "Promoted")]
    public async Task HandleAsync_Titles_Entry_By_Promotion_Source(string? sourceReference, string expectedTitle)
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var previousPosition = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineer", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        var newPosition = PositionProfile.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), "Engineering Manager", null, null, null, null, null, null, null, Guid.NewGuid(), now);
        context.PositionProfiles.AddRange(previousPosition, newPosition);
        var employeeId = Guid.NewGuid();
        var effectiveDate = new DateOnly(2026, 8, 1);
        var promotion = EmployeePromotion.Create(
            Guid.NewGuid(), companyId, employeeId, previousPosition.Id, newPosition.Id,
            newManagerId: null, newLocationId: null, effectiveDate, "Reason.", notes: null,
            compensationId: null, Guid.NewGuid(), now, sourceReference: sourceReference);
        context.EmployeePromotions.Add(promotion);
        await context.SaveChangesAsync();
        var timelineWriter = new FakeEmployeeTimelineWriter();

        await new EmployeePromotedHandler(context, timelineWriter).HandleAsync(
            new EmployeePromotedIntegrationEvent(companyId, employeeId, previousPosition.Id, newPosition.Id, effectiveDate, promotion.Id),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(expectedTitle, entry.Title);
        Assert.Contains("Engineer", entry.Summary);
        Assert.Contains("Engineering Manager", entry.Summary);
        Assert.Equal(promotion.Id, entry.SourceRecordId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Treat_Another_Companys_Internal_Appointment_As_Internal()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var promotion = EmployeePromotion.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            newManagerId: null, newLocationId: null, new DateOnly(2026, 8, 1), "Reason.", notes: null,
            compensationId: null, Guid.NewGuid(), now, sourceReference: $"recruitment:application:{Guid.NewGuid()}");
        context.EmployeePromotions.Add(promotion);
        await context.SaveChangesAsync();
        var timelineWriter = new FakeEmployeeTimelineWriter();

        await new EmployeePromotedHandler(context, timelineWriter).HandleAsync(
            new EmployeePromotedIntegrationEvent(Guid.NewGuid(), promotion.EmployeeId, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 1), promotion.Id),
            CancellationToken.None);

        Assert.Equal("Promoted", Assert.Single(timelineWriter.Added).Title);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
