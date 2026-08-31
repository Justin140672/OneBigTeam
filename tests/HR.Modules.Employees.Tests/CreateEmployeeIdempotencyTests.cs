using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateEmployee;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

/// <summary>
/// NFR-08: an automated multi-step workflow (e.g. candidate hire) provisions an employee in the
/// Employees DbContext, then commits its own module state separately. If the second commit fails
/// and the workflow is retried, provisioning must NOT create a second employee (and therefore must
/// not re-publish EmployeeCreated, which would duplicate onboarding plans, probation records,
/// leave initialisation and notifications). This is guaranteed by a stable SourceReference key.
/// </summary>
public class CreateEmployeeIdempotencyTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 7, 1);
    private static readonly DateOnly DateOfBirth = new(1995, 3, 20);

    [Fact]
    public async Task Retrying_With_Same_SourceReference_Returns_Same_Employee_And_Publishes_Event_Once()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (departmentId, locationId, employmentTypeId, positionProfileId) = await SeedAsync(context, companyId);

        var publisher = new CapturingIntegrationEventPublisher();
        var handler = BuildHandler(context, publisher);

        const string sourceRef = "recruitment:application:11111111-1111-1111-1111-111111111111";

        var first = await handler.HandleAsync(
            RequestFor(companyId, departmentId, locationId, employmentTypeId, positionProfileId,
                "emma.clarke@example.com", "EMP-0001", sourceRef),
            CancellationToken.None);

        // Simulates the retry: a fresh request id / employee number, same logical source.
        var second = await handler.HandleAsync(
            RequestFor(companyId, departmentId, locationId, employmentTypeId, positionProfileId,
                "emma.clarke.retry@example.com", "EMP-0002", sourceRef),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.Id, second.Value!.Id);

        Assert.Single(await context.Employees.ToListAsync());
        Assert.Single(publisher.Published.OfType<EmployeeCreatedIntegrationEvent>());
    }

    [Fact]
    public async Task Different_SourceReference_Creates_Distinct_Employees()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (departmentId, locationId, employmentTypeId, positionProfileId) = await SeedAsync(context, companyId);

        var publisher = new CapturingIntegrationEventPublisher();
        var handler = BuildHandler(context, publisher);

        var a = await handler.HandleAsync(
            RequestFor(companyId, departmentId, locationId, employmentTypeId, positionProfileId,
                "a@example.com", "EMP-0001", "recruitment:application:aaaa"),
            CancellationToken.None);
        var b = await handler.HandleAsync(
            RequestFor(companyId, departmentId, locationId, employmentTypeId, positionProfileId,
                "b@example.com", "EMP-0002", "recruitment:application:bbbb"),
            CancellationToken.None);

        Assert.True(a.IsSuccess);
        Assert.True(b.IsSuccess);
        Assert.NotEqual(a.Value!.Id, b.Value!.Id);
        Assert.Equal(2, await context.Employees.CountAsync());
        Assert.Equal(2, publisher.Published.OfType<EmployeeCreatedIntegrationEvent>().Count());
    }

    [Fact]
    public async Task No_SourceReference_Behaves_As_Before()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (departmentId, locationId, employmentTypeId, positionProfileId) = await SeedAsync(context, companyId);

        var handler = BuildHandler(context, new CapturingIntegrationEventPublisher());

        var result = await handler.HandleAsync(
            RequestFor(companyId, departmentId, locationId, employmentTypeId, positionProfileId,
                "plain@example.com", "EMP-0001", sourceReference: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await context.Employees.SingleAsync();
        Assert.Null(saved.SourceReference);
    }

    [Fact]
    public void Employee_Create_Trims_And_Nulls_Blank_SourceReference()
    {
        var withRef = Employee.Create(
            Guid.NewGuid(), Guid.NewGuid(), "A", "B", "a@b.com", StartDate, true,
            DateOfBirth, "British", "Female", "EMP-1", Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Now, "  recruitment:application:x  ");
        Assert.Equal("recruitment:application:x", withRef.SourceReference);

        var blank = Employee.Create(
            Guid.NewGuid(), Guid.NewGuid(), "A", "B", "a@b.com", StartDate, true,
            DateOfBirth, "British", "Female", "EMP-1", Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Now, "   ");
        Assert.Null(blank.SourceReference);
    }

    private static CreateEmployeeHandler BuildHandler(EmployeesDbContext context, IIntegrationEventPublisher publisher) =>
        new(context, new FakeClock(FixedUtcNow), publisher,
            new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(),
            new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());

    private static CreateEmployeeRequest RequestFor(
        Guid companyId, Guid departmentId, Guid locationId, Guid employmentTypeId, Guid positionProfileId,
        string workEmail, string employeeNumber, string? sourceReference) =>
        new()
        {
            CompanyId = companyId,
            DepartmentId = departmentId,
            LocationId = locationId,
            PositionProfileId = positionProfileId,
            FirstName = "Emma",
            LastName = "Clarke",
            WorkEmail = workEmail,
            StartDate = StartDate,
            DateOfBirth = DateOfBirth,
            Nationality = "British",
            Gender = "Female",
            EmployeeNumber = employeeNumber,
            EmploymentTypeId = employmentTypeId,
            SourceReference = sourceReference,
        };

    private static async Task<(Guid DepartmentId, Guid LocationId, Guid EmploymentTypeId, Guid PositionProfileId)> SeedAsync(
        EmployeesDbContext context, Guid companyId)
    {
        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, Now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, Now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, Now);
        var positionProfile = PositionProfile.Create(
            Guid.NewGuid(), companyId, department.Id, location.Id, "Developer",
            null, null, null, null, null, null, null, Guid.NewGuid(), Now);
        var employmentType = EmploymentType.Create(Guid.NewGuid(), companyId, "Permanent", null, Now);

        context.Departments.Add(department);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        context.PositionProfiles.Add(positionProfile);
        context.EmploymentTypes.Add(employmentType);
        await context.SaveChangesAsync();

        return (department.Id, location.Id, employmentType.Id, positionProfile.Id);
    }

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
