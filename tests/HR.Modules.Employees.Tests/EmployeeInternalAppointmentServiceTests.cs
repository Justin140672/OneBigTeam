using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateTimelineEntryOnEmployeePromoted;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Employees.Tests;

/// <summary>
/// Internal recruitment Ticket 7: <see cref="EmployeeInternalAppointmentService"/> moves an EXISTING
/// employee into the role of an internal vacancy through the promotion mechanism. It never creates an
/// employee, is idempotent on the caller's source reference, and applies a due change immediately
/// while scheduling a future-dated one.
/// </summary>
public class EmployeeInternalAppointmentServiceTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly DateOnly StartDate = new(2019, 4, 1);
    private static readonly DateOnly ContinuousServiceDate = new(2018, 11, 5);

    private sealed class Harness
    {
        public required EmployeesDbContext Db { get; init; }
        public required Guid CompanyId { get; init; }
        public required Employee Employee { get; init; }
        public required Employee Manager { get; init; }
        public required PositionProfile CurrentProfile { get; init; }
        public required PositionProfile NewProfile { get; init; }
        public FakeAuditPublisher Audit { get; } = new();
        public CapturingIntegrationEventPublisher Events { get; } = new();
        public FakeEmployeeTimelineWriter Timeline { get; } = new();

        public string SourceReference { get; } = $"recruitment:application:{Guid.NewGuid()}";

        public EmployeeInternalAppointmentService Service()
        {
            var clock = new FakeClock(FixedUtcNow);
            return new EmployeeInternalAppointmentService(
                Db,
                clock,
                new FakeCompanyTimeZoneReader(),
                new CompensationRecordWriter(Db, clock),
                Audit,
                new EmployeePromotionFinalizer(Db, Audit, Events),
                Timeline,
                NullLogger<EmployeeInternalAppointmentService>.Instance);
        }

        public InternalAppointmentRequest Request(
            DateOnly? effectiveDate = null,
            Guid? managerId = null,
            bool noManager = false,
            bool confirmBackdated = false,
            InternalAppointmentCompensation? compensation = null,
            string? sourceReference = null) =>
            new(
                CompanyId,
                Employee.Id,
                NewProfile.Id,
                effectiveDate ?? Today,
                noManager ? null : managerId ?? Manager.Id,
                sourceReference ?? SourceReference,
                "Internal appointment: Engineering Manager",
                Guid.NewGuid(),
                confirmBackdated,
                compensation);

        public async Task<Employee> ReloadEmployeeAsync()
        {
            Db.ChangeTracker.Clear();
            return await Db.Employees.SingleAsync(e => e.Id == Employee.Id);
        }
    }

    private static Employee NewEmployee(Guid companyId, string email, string number, Guid positionProfileId, Guid departmentId, Guid locationId)
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Priya", "Shah", email, StartDate,
            hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", number,
            Guid.NewGuid(), departmentId, locationId, positionProfileId, Now.AddYears(-1));
        employee.Activate(Now.AddYears(-1));
        return employee;
    }

    private static PositionProfile NewProfile(Guid companyId, string title, Guid departmentId, Guid locationId) =>
        PositionProfile.Create(Guid.NewGuid(), companyId, departmentId, locationId, title, null, null, null, null, null, null, Guid.NewGuid(), Now.AddYears(-1));

    private static async Task<Harness> SeedAsync(Action<Harness>? configure = null)
    {
        var db = new EmployeesDbContext(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var companyId = Guid.NewGuid();

        var currentProfile = NewProfile(companyId, "Senior Software Engineer", Guid.NewGuid(), Guid.NewGuid());
        var newProfile = NewProfile(companyId, "Engineering Manager", Guid.NewGuid(), Guid.NewGuid());
        db.PositionProfiles.AddRange(currentProfile, newProfile);

        var oldManager = NewEmployee(companyId, "old.manager@acme.example", "EMP-0001", Guid.NewGuid(), currentProfile.DepartmentId, currentProfile.LocationId);
        var newManager = NewEmployee(companyId, "new.manager@acme.example", "EMP-0002", Guid.NewGuid(), newProfile.DepartmentId, newProfile.LocationId);
        var employee = NewEmployee(companyId, "priya.shah@acme.example", "EMP-0042", currentProfile.Id, currentProfile.DepartmentId, currentProfile.LocationId);
        employee.UpdateEmploymentDetails("EMP-0042", employee.EmploymentTypeId, StartDate, ContinuousServiceDate, null, null, null, Now.AddYears(-1));
        employee.Assign(employee.DepartmentId, employee.PositionProfileId, employee.LocationId, oldManager.Id, Now.AddYears(-1));
        db.Employees.AddRange(oldManager, newManager, employee);

        var harness = new Harness
        {
            Db = db,
            CompanyId = companyId,
            Employee = employee,
            Manager = newManager,
            CurrentProfile = currentProfile,
            NewProfile = newProfile,
        };
        configure?.Invoke(harness);
        await db.SaveChangesAsync();
        return harness;
    }

    private static InternalAppointmentCompensation Compensation(string salaryType = "Annual") =>
        new(salaryType, 72000m, "gbp", 37.5m, 1m, "New role salary.");


    [Fact]
    public async Task AppointAsync_Applies_Position_Department_Location_And_Manager_When_Due_Today()
    {
        var h = await SeedAsync();

        var result = await h.Service().AppointAsync(h.Request(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var value = result.Value!;
        Assert.True(value.IsApplied);
        Assert.False(value.WasAlreadyRecorded);
        Assert.Equal(h.Employee.Id, value.EmployeeId);
        Assert.Equal(h.CurrentProfile.Id, value.PreviousPositionProfileId);
        Assert.Equal(h.NewProfile.Id, value.NewPositionProfileId);
        Assert.Equal(h.NewProfile.DepartmentId, value.NewDepartmentId);
        Assert.Equal(h.NewProfile.LocationId, value.NewLocationId);
        Assert.Equal(h.Manager.Id, value.NewManagerId);
        Assert.Equal(Today, value.EffectiveDate);

        var employee = await h.ReloadEmployeeAsync();
        Assert.Equal(h.NewProfile.Id, employee.PositionProfileId);
        Assert.Equal(h.NewProfile.DepartmentId, employee.DepartmentId);
        Assert.Equal(h.NewProfile.LocationId, employee.LocationId);
        Assert.Equal(h.Manager.Id, employee.ManagerId);

        var promotion = await h.Db.EmployeePromotions.SingleAsync();
        Assert.Equal(value.PromotionId, promotion.Id);
        Assert.NotNull(promotion.CompletedAt);
        Assert.Equal(h.SourceReference, promotion.SourceReference);
        Assert.True(promotion.IsInternalAppointment);
        Assert.Equal(h.NewProfile.DepartmentId, promotion.NewDepartmentId);
        Assert.False(promotion.ClearsManager);
        Assert.Equal("Internal appointment: Engineering Manager", promotion.Reason);
    }

    [Fact]
    public async Task AppointAsync_Preserves_Employment_Identity_And_Creates_No_Employee()
    {
        var h = await SeedAsync();
        var employeesBefore = await h.Db.Employees.CountAsync();

        var result = await h.Service().AppointAsync(h.Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var employee = await h.ReloadEmployeeAsync();
        Assert.Equal("EMP-0042", employee.EmployeeNumber);
        Assert.Equal(StartDate, employee.StartDate);
        Assert.Equal(ContinuousServiceDate, employee.ContinuousServiceDate);
        Assert.Equal(EmploymentStatus.Active, employee.Status);
        Assert.Equal("priya.shah@acme.example", employee.WorkEmail);
        Assert.Equal("Priya", employee.FirstName);
        Assert.Equal(employeesBefore, await h.Db.Employees.CountAsync());

        Assert.Empty(h.Events.Published.OfType<EmployeeCreatedIntegrationEvent>());
    }

    [Fact]
    public async Task AppointAsync_Publishes_Promoted_Manager_And_Location_Changed_Events_When_Applied()
    {
        var h = await SeedAsync();
        var previousManagerId = h.Employee.ManagerId;
        var previousLocationId = h.Employee.LocationId;

        var result = await h.Service().AppointAsync(h.Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var promoted = Assert.Single(h.Events.Published.OfType<EmployeePromotedIntegrationEvent>());
        Assert.Equal(result.Value!.PromotionId, promoted.PromotionId);

        var managerChanged = Assert.Single(h.Events.Published.OfType<EmployeeManagerChangedIntegrationEvent>());
        Assert.Equal(previousManagerId, managerChanged.PreviousManagerId);
        Assert.Equal(h.Manager.Id, managerChanged.NewManagerId);

        var locationChanged = Assert.Single(h.Events.Published.OfType<EmployeeLocationChangedIntegrationEvent>());
        Assert.Equal(previousLocationId, locationChanged.PreviousLocationId);
        Assert.Equal(h.NewProfile.LocationId, locationChanged.NewLocationId);

        Assert.Single(h.Audit.Published.OfType<EmployeePromotionRequestedAuditEvent>());
        Assert.Single(h.Audit.Published.OfType<EmployeePromotionCompletedAuditEvent>());
    }

    [Fact]
    public async Task AppointAsync_Immediate_Change_Is_Described_As_Internal_Appointment_On_Timeline()
    {
        var h = await SeedAsync();

        var result = await h.Service().AppointAsync(h.Request(), CancellationToken.None);
        Assert.True(result.IsSuccess);

        var consumerTimeline = new FakeEmployeeTimelineWriter();
        var promoted = Assert.Single(h.Events.Published.OfType<EmployeePromotedIntegrationEvent>());
        await new EmployeePromotedHandler(h.Db, consumerTimeline).HandleAsync(promoted, CancellationToken.None);

        var entry = Assert.Single(consumerTimeline.Added);
        Assert.Equal("Internal appointment", entry.Title);
        Assert.Contains("Senior Software Engineer", entry.Summary);
        Assert.Contains("Engineering Manager", entry.Summary);
        Assert.Equal(result.Value!.PromotionId, entry.SourceRecordId);
        Assert.Equal(EmployeeTimelineEventType.EmployeePromoted, entry.EventType);
    }

    [Fact]
    public async Task AppointAsync_Backdated_With_Confirmation_Is_Applied_Immediately()
    {
        var h = await SeedAsync();

        var result = await h.Service().AppointAsync(
            h.Request(effectiveDate: Today.AddDays(-1), confirmBackdated: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsApplied);
        Assert.Equal(h.NewProfile.Id, (await h.ReloadEmployeeAsync()).PositionProfileId);
    }

    [Fact]
    public async Task AppointAsync_Today_Does_Not_Require_Backdate_Confirmation()
    {
        var h = await SeedAsync();

        var result = await h.Service().AppointAsync(
            h.Request(effectiveDate: Today, confirmBackdated: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsApplied);
    }


    [Fact]
    public async Task AppointAsync_Future_Dated_Is_Scheduled_Without_Changing_Employee()
    {
        var h = await SeedAsync();
        var effective = Today.AddDays(1);
        var before = (h.Employee.PositionProfileId, h.Employee.DepartmentId, h.Employee.LocationId, h.Employee.ManagerId);

        var result = await h.Service().AppointAsync(h.Request(effectiveDate: effective), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsApplied);

        var employee = await h.ReloadEmployeeAsync();
        Assert.Equal(before, (employee.PositionProfileId, employee.DepartmentId, employee.LocationId, employee.ManagerId));

        var promotion = await h.Db.EmployeePromotions.SingleAsync();
        Assert.Null(promotion.CompletedAt);
        Assert.Equal(effective, promotion.EffectiveDate);

        Assert.Empty(h.Events.Published.OfType<EmployeePromotedIntegrationEvent>());
        Assert.Empty(h.Events.Published.OfType<EmployeeManagerChangedIntegrationEvent>());
        Assert.Empty(h.Audit.Published.OfType<EmployeePromotionCompletedAuditEvent>());

        var entry = Assert.Single(h.Timeline.Added);
        Assert.Equal("Internal appointment", entry.Title);
        Assert.Equal(EmployeeTimelineEventType.EmployeePromoted, entry.EventType);
        Assert.Equal(promotion.Id, entry.SourceRecordId);
        Assert.Equal(effective, entry.EventDate);
        Assert.Equal(h.Employee.Id, entry.EmployeeId);
        Assert.Contains("Senior Software Engineer", entry.Summary);
        Assert.Contains("Engineering Manager", entry.Summary);
    }


    [Fact]
    public async Task AppointAsync_Retry_With_Same_Source_Reference_Returns_Same_Change_And_Records_Nothing_New()
    {
        var h = await SeedAsync();
        var service = h.Service();

        var first = await service.AppointAsync(h.Request(compensation: Compensation()), CancellationToken.None);
        var second = await h.Service().AppointAsync(h.Request(compensation: Compensation()), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.PromotionId, second.Value!.PromotionId);
        Assert.Equal(first.Value.CompensationId, second.Value.CompensationId);
        Assert.False(first.Value.WasAlreadyRecorded);
        Assert.True(second.Value.WasAlreadyRecorded);
        Assert.True(second.Value.IsApplied);

        Assert.Equal(1, await h.Db.EmployeePromotions.CountAsync());
        Assert.Equal(1, await h.Db.Compensations.CountAsync(c => c.EmployeeId == h.Employee.Id));
        Assert.Single(h.Events.Published.OfType<EmployeePromotedIntegrationEvent>());
    }

    [Fact]
    public async Task AppointAsync_Retry_Does_Not_Reapply_Different_Values()
    {
        var h = await SeedAsync();
        var first = await h.Service().AppointAsync(h.Request(), CancellationToken.None);

        var second = await h.Service().AppointAsync(
            h.Request(noManager: true, effectiveDate: Today.AddDays(30)), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.PromotionId, second.Value!.PromotionId);
        Assert.Equal(h.Manager.Id, second.Value.NewManagerId);
        Assert.Equal(Today, second.Value.EffectiveDate);
        Assert.Equal(h.Manager.Id, (await h.ReloadEmployeeAsync()).ManagerId);
    }

    [Fact]
    public async Task AppointAsync_Retry_Of_Scheduled_Change_Does_Not_Record_Or_Apply_A_Second_Change()
    {
        var h = await SeedAsync();
        var effective = Today.AddDays(10);

        var first = await h.Service().AppointAsync(h.Request(effectiveDate: effective), CancellationToken.None);
        var second = await h.Service().AppointAsync(h.Request(effectiveDate: effective), CancellationToken.None);

        Assert.Equal(first.Value!.PromotionId, second.Value!.PromotionId);
        Assert.False(second.Value.IsApplied);
        Assert.True(second.Value.WasAlreadyRecorded);
        Assert.Equal(1, await h.Db.EmployeePromotions.CountAsync());
        Assert.Null((await h.Db.EmployeePromotions.SingleAsync()).CompletedAt);
        Assert.Equal(h.CurrentProfile.Id, (await h.ReloadEmployeeAsync()).PositionProfileId);
    }

    [Fact]
    public async Task ResumeBySourceReferenceAsync_Finalizes_A_Due_But_Uncompleted_Change()
    {
        EmployeePromotion? interrupted = null;
        var h = await SeedAsync(harness =>
        {
            interrupted = EmployeePromotion.Create(
                Guid.NewGuid(), harness.CompanyId, harness.Employee.Id, harness.CurrentProfile.Id, harness.NewProfile.Id,
                newManagerId: null, harness.NewProfile.LocationId, Today, "Internal appointment: Engineering Manager",
                notes: null, compensationId: null, Guid.NewGuid(), Now.AddMinutes(-20),
                newDepartmentId: harness.NewProfile.DepartmentId, clearsManager: true, sourceReference: harness.SourceReference);
            harness.Db.EmployeePromotions.Add(interrupted);
        });

        var resumed = await h.Service().ResumeBySourceReferenceAsync(h.CompanyId, h.SourceReference, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(resumed);
        Assert.Equal(interrupted!.Id, resumed.PromotionId);
        Assert.True(resumed.IsApplied);
        Assert.True(resumed.WasAlreadyRecorded);
        Assert.Null(resumed.NewManagerId);

        var employee = await h.ReloadEmployeeAsync();
        Assert.Equal(h.NewProfile.Id, employee.PositionProfileId);
        Assert.Equal(h.NewProfile.DepartmentId, employee.DepartmentId);
        Assert.Equal(h.NewProfile.LocationId, employee.LocationId);
        Assert.Null(employee.ManagerId);
        Assert.NotNull((await h.Db.EmployeePromotions.SingleAsync()).CompletedAt);
        Assert.Equal(1, await h.Db.EmployeePromotions.CountAsync());
    }

    [Fact]
    public async Task AppointAsync_Retry_Finalizes_A_Due_But_Uncompleted_Change()
    {
        var h = await SeedAsync(harness =>
            harness.Db.EmployeePromotions.Add(EmployeePromotion.Create(
                Guid.NewGuid(), harness.CompanyId, harness.Employee.Id, harness.CurrentProfile.Id, harness.NewProfile.Id,
                harness.Manager.Id, harness.NewProfile.LocationId, Today, "Internal appointment: Engineering Manager",
                notes: null, compensationId: null, Guid.NewGuid(), Now.AddMinutes(-20),
                newDepartmentId: harness.NewProfile.DepartmentId, sourceReference: harness.SourceReference)));

        var result = await h.Service().AppointAsync(h.Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.WasAlreadyRecorded);
        Assert.True(result.Value.IsApplied);
        Assert.Equal(h.NewProfile.Id, (await h.ReloadEmployeeAsync()).PositionProfileId);
        Assert.Equal(1, await h.Db.EmployeePromotions.CountAsync());
    }

    [Fact]
    public async Task ResumeBySourceReferenceAsync_Does_Not_Refinalize_A_Completed_Change()
    {
        var h = await SeedAsync();
        var first = await h.Service().AppointAsync(h.Request(), CancellationToken.None);
        var eventsBefore = h.Events.Published.Count;

        var resumed = await h.Service().ResumeBySourceReferenceAsync(h.CompanyId, h.SourceReference, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(resumed);
        Assert.Equal(first.Value!.PromotionId, resumed.PromotionId);
        Assert.True(resumed.IsApplied);
        Assert.Equal(eventsBefore, h.Events.Published.Count);
    }

    [Fact]
    public async Task ResumeBySourceReferenceAsync_Leaves_A_Future_Change_Scheduled()
    {
        var h = await SeedAsync();
        await h.Service().AppointAsync(h.Request(effectiveDate: Today.AddDays(5)), CancellationToken.None);

        var resumed = await h.Service().ResumeBySourceReferenceAsync(h.CompanyId, h.SourceReference, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(resumed);
        Assert.False(resumed.IsApplied);
        Assert.Null((await h.Db.EmployeePromotions.SingleAsync()).CompletedAt);
        Assert.Equal(h.CurrentProfile.Id, (await h.ReloadEmployeeAsync()).PositionProfileId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("recruitment:application:unknown")]
    public async Task ResumeBySourceReferenceAsync_Returns_Null_When_Nothing_Recorded(string sourceReference)
    {
        var h = await SeedAsync();
        await h.Service().AppointAsync(h.Request(), CancellationToken.None);

        var resumed = await h.Service().ResumeBySourceReferenceAsync(h.CompanyId, sourceReference, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(resumed);
    }

    [Fact]
    public async Task ResumeBySourceReferenceAsync_Is_Company_Scoped()
    {
        var h = await SeedAsync();
        await h.Service().AppointAsync(h.Request(), CancellationToken.None);

        var resumed = await h.Service().ResumeBySourceReferenceAsync(Guid.NewGuid(), h.SourceReference, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(resumed);
    }

    [Fact]
    public async Task AppointAsync_Trims_Source_Reference_So_A_Padded_Retry_Matches()
    {
        var h = await SeedAsync();
        var first = await h.Service().AppointAsync(h.Request(), CancellationToken.None);

        var second = await h.Service().AppointAsync(h.Request(sourceReference: $"  {h.SourceReference}  "), CancellationToken.None);

        Assert.Equal(first.Value!.PromotionId, second.Value!.PromotionId);
        Assert.Equal(1, await h.Db.EmployeePromotions.CountAsync());
    }


    [Fact]
    public async Task AppointAsync_Without_Manager_Leaves_Employee_With_No_Manager()
    {
        var h = await SeedAsync();
        Assert.NotNull(h.Employee.ManagerId);

        var result = await h.Service().AppointAsync(h.Request(noManager: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.NewManagerId);
        Assert.Null((await h.ReloadEmployeeAsync()).ManagerId);

        var promotion = await h.Db.EmployeePromotions.SingleAsync();
        Assert.True(promotion.ClearsManager);
        Assert.Null(promotion.NewManagerId);

        var changed = Assert.Single(h.Events.Published.OfType<EmployeeManagerChangedIntegrationEvent>());
        Assert.Null(changed.NewManagerId);
    }

    [Fact]
    public async Task AppointAsync_Returns_Validation_When_Manager_Is_The_Employee()
    {
        var h = await SeedAsync();

        var result = await h.Service().AppointAsync(h.Request(managerId: h.Employee.Id), CancellationToken.None);

        await AssertNothingRecordedAsync(h, result, "validation");
    }

    [Fact]
    public async Task AppointAsync_Returns_NotFound_When_Manager_Does_Not_Exist()
    {
        var h = await SeedAsync();

        var result = await h.Service().AppointAsync(h.Request(managerId: Guid.NewGuid()), CancellationToken.None);

        await AssertNothingRecordedAsync(h, result, "not_found");
    }

    [Fact]
    public async Task AppointAsync_Returns_NotFound_When_Manager_Is_A_Former_Employee()
    {
        var h = await SeedAsync(harness => harness.Manager.SetStatusForTesting(EmploymentStatus.FormerEmployee, Now));

        var result = await h.Service().AppointAsync(h.Request(), CancellationToken.None);

        await AssertNothingRecordedAsync(h, result, "not_found");
    }

    [Fact]
    public async Task AppointAsync_Returns_NotFound_When_Manager_Belongs_To_Another_Company()
    {
        var h = await SeedAsync(harness =>
        {
            var foreign = NewEmployee(Guid.NewGuid(), "foreign@other.example", "EMP-9", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            harness.Db.Employees.Add(foreign);
        });
        var foreignId = (await h.Db.Employees.SingleAsync(e => e.CompanyId != h.CompanyId)).Id;

        var result = await h.Service().AppointAsync(h.Request(managerId: foreignId), CancellationToken.None);

        await AssertNothingRecordedAsync(h, result, "not_found");
    }

    [Fact]
    public async Task AppointAsync_Returns_Conflict_When_Manager_Would_Create_A_Cycle()
    {
        var h = await SeedAsync(harness =>
            harness.Manager.Assign(harness.Manager.DepartmentId, harness.Manager.PositionProfileId, harness.Manager.LocationId, harness.Employee.Id, Now));

        var result = await h.Service().AppointAsync(h.Request(), CancellationToken.None);

        await AssertNothingRecordedAsync(h, result, "conflict");
    }


    [Theory]
    [InlineData("Suspended")]
    [InlineData("Leaving")]
    [InlineData("FormerEmployee")]
    public async Task AppointAsync_Returns_Validation_When_Employee_Not_Active(string status)
    {
        var h = await SeedAsync(harness => harness.Employee.SetStatusForTesting(Enum.Parse<EmploymentStatus>(status), Now));

        var result = await h.Service().AppointAsync(h.Request(), CancellationToken.None);

        await AssertNothingRecordedAsync(h, result, "validation");
    }

    [Fact]
    public async Task AppointAsync_Returns_NotFound_When_Employee_Missing()
    {
        var h = await SeedAsync();

        var result = await h.Service().AppointAsync(h.Request() with { EmployeeId = Guid.NewGuid() }, CancellationToken.None);

        await AssertNothingRecordedAsync(h, result, "not_found");
    }

    [Fact]
    public async Task AppointAsync_Returns_NotFound_When_Employee_In_Another_Company()
    {
        var h = await SeedAsync();

        var result = await h.Service().AppointAsync(h.Request() with { CompanyId = Guid.NewGuid() }, CancellationToken.None);

        await AssertNothingRecordedAsync(h, result, "not_found");
    }

    [Fact]
    public async Task AppointAsync_Returns_NotFound_When_Position_Profile_Missing()
    {
        var h = await SeedAsync();

        var result = await h.Service().AppointAsync(h.Request() with { NewPositionProfileId = Guid.NewGuid() }, CancellationToken.None);

        await AssertNothingRecordedAsync(h, result, "not_found");
    }

    [Fact]
    public async Task AppointAsync_Returns_NotFound_When_Position_Profile_Belongs_To_Another_Company()
    {
        var h = await SeedAsync(harness =>
            harness.Db.PositionProfiles.Add(NewProfile(Guid.NewGuid(), "Foreign Role", Guid.NewGuid(), Guid.NewGuid())));
        var foreignProfileId = (await h.Db.PositionProfiles.SingleAsync(p => p.CompanyId != h.CompanyId)).Id;

        var result = await h.Service().AppointAsync(h.Request() with { NewPositionProfileId = foreignProfileId }, CancellationToken.None);

        await AssertNothingRecordedAsync(h, result, "not_found");
    }

    [Fact]
    public async Task AppointAsync_Returns_Conflict_When_Backdated_Without_Confirmation()
    {
        var h = await SeedAsync();

        var result = await h.Service().AppointAsync(
            h.Request(effectiveDate: Today.AddDays(-1), confirmBackdated: false), CancellationToken.None);

        await AssertNothingRecordedAsync(h, result, "conflict");
    }

    [Theory]
    [InlineData("Weekly")]
    [InlineData("Monthly")]
    [InlineData("")]
    public async Task AppointAsync_Returns_Validation_For_Invalid_Salary_Type(string salaryType)
    {
        var h = await SeedAsync();

        var result = await h.Service().AppointAsync(h.Request(compensation: Compensation(salaryType)), CancellationToken.None);

        await AssertNothingRecordedAsync(h, result, "validation");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AppointAsync_Returns_Validation_When_Source_Reference_Blank(string sourceReference)
    {
        var h = await SeedAsync();

        var result = await h.Service().AppointAsync(h.Request(sourceReference: sourceReference), CancellationToken.None);

        await AssertNothingRecordedAsync(h, result, "validation");
    }


    [Theory]
    [InlineData("Annual", "Annual")]
    [InlineData("hourly", "Hourly")]
    [InlineData("DAILY", "Daily")]
    public async Task AppointAsync_Records_Role_Change_Compensation_From_Effective_Date(string salaryTypeName, string expected)
    {
        var h = await SeedAsync();
        var effective = Today.AddDays(7);

        var result = await h.Service().AppointAsync(
            h.Request(effectiveDate: effective, compensation: Compensation(salaryTypeName)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var compensation = await h.Db.Compensations.SingleAsync(c => c.EmployeeId == h.Employee.Id);
        Assert.Equal(result.Value!.CompensationId, compensation.Id);
        Assert.Equal(CompensationChangeReason.RoleChange, compensation.Reason);
        Assert.Equal(effective, compensation.EffectiveFrom);
        Assert.Equal(Enum.Parse<SalaryType>(expected), compensation.SalaryType);
        Assert.Equal(72000m, compensation.Salary);
        Assert.Equal("GBP", compensation.Currency);
        Assert.Equal(37.5m, compensation.HoursPerWeek);
        Assert.Equal(1m, compensation.FTE);
        Assert.Equal(compensation.Id, (await h.Db.EmployeePromotions.SingleAsync()).CompensationId);
    }

    [Fact]
    public async Task AppointAsync_Without_Compensation_Records_None()
    {
        var h = await SeedAsync();

        var result = await h.Service().AppointAsync(h.Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.CompensationId);
        Assert.Equal(0, await h.Db.Compensations.CountAsync());
    }


    private static async Task AssertNothingRecordedAsync(Harness h, Result<InternalAppointmentResult> result, string expectedCode)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);

        var employeeBefore = (h.Employee.PositionProfileId, h.Employee.DepartmentId, h.Employee.LocationId, h.Employee.ManagerId);
        h.Db.ChangeTracker.Clear();
        Assert.Equal(0, await h.Db.EmployeePromotions.CountAsync());
        Assert.Equal(0, await h.Db.Compensations.CountAsync());
        var employee = await h.Db.Employees.SingleAsync(e => e.Id == h.Employee.Id);
        Assert.Equal(employeeBefore, (employee.PositionProfileId, employee.DepartmentId, employee.LocationId, employee.ManagerId));
        Assert.Empty(h.Events.Published);
        Assert.Empty(h.Audit.Published);
        Assert.Empty(h.Timeline.Added);
    }
}
