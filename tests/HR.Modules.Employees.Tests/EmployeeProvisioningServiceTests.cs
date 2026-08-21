using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateEmployee;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class EmployeeProvisioningServiceTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 7, 1);
    private static readonly DateOnly DateOfBirth = new(1995, 3, 20);

    [Fact]
    public async Task CreateFromCandidateAsync_Creates_Employee_And_Returns_Id()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (departmentId, locationId, employmentTypeId, positionProfileId) = await SeedMandatoryLookupsAsync(context, companyId);

        var createEmployeeHandler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(),
            new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var service = new EmployeeProvisioningService(createEmployeeHandler, context, new FakeClock(FixedUtcNow));

        var result = await service.CreateFromCandidateAsync(
            new EmployeeProvisioningRequest(
                companyId, "Emma", "Clarke", "emma.clarke@example.com",
                StartDate, DateOfBirth, "British", "Female",
                "EMP-0001", employmentTypeId, departmentId, locationId, positionProfileId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);

        var saved = await context.Employees.SingleAsync();
        Assert.Equal(result.Value, saved.Id);
        Assert.Equal("Emma", saved.FirstName);
        Assert.Equal("Clarke", saved.LastName);
        Assert.Equal("emma.clarke@example.com", saved.WorkEmail);
    }

    [Fact]
    public async Task CreateFromCandidateAsync_Returns_Failure_When_Underlying_Handler_Fails()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (departmentId, locationId, employmentTypeId, positionProfileId) = await SeedMandatoryLookupsAsync(context, companyId);

        var createEmployeeHandler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(),
            new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var service = new EmployeeProvisioningService(createEmployeeHandler, context, new FakeClock(FixedUtcNow));

        await service.CreateFromCandidateAsync(
            new EmployeeProvisioningRequest(
                companyId, "Emma", "Clarke", "emma.clarke@example.com",
                StartDate, DateOfBirth, "British", "Female",
                "EMP-0001", employmentTypeId, departmentId, locationId, positionProfileId),
            CancellationToken.None);

        // Same work email in the same company should conflict.
        var result = await service.CreateFromCandidateAsync(
            new EmployeeProvisioningRequest(
                companyId, "Emma", "Clarke", "emma.clarke@example.com",
                StartDate, DateOfBirth, "British", "Female",
                "EMP-0002", employmentTypeId, departmentId, locationId, positionProfileId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task MarkAsInitialCompanyAdminAsync_Sets_RequiresInitialSetup_And_Seeds_Placeholder_Compensation()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (departmentId, locationId, employmentTypeId, positionProfileId) = await SeedMandatoryLookupsAsync(context, companyId);

        var createEmployeeHandler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(),
            new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var service = new EmployeeProvisioningService(createEmployeeHandler, context, new FakeClock(FixedUtcNow));

        var created = await service.CreateFromCandidateAsync(
            new EmployeeProvisioningRequest(
                companyId, "Ada", "Lovelace", "ada@example.com",
                StartDate, DateOfBirth, "British", "Female",
                "EMP-0001", employmentTypeId, departmentId, locationId, positionProfileId),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        await service.MarkAsInitialCompanyAdminAsync(companyId, created.Value, CancellationToken.None);

        var saved = await context.Employees.SingleAsync(e => e.Id == created.Value);
        Assert.True(saved.IsInitialCompanyAdmin);
        Assert.True(saved.RequiresInitialSetup);

        var compensation = await context.Compensations.SingleAsync(c => c.EmployeeId == created.Value);
        Assert.Equal(SalaryType.Annual, compensation.SalaryType);
        Assert.Equal(0m, compensation.Salary);
        Assert.Equal("GBP", compensation.Currency);
        Assert.Equal(CompensationChangeReason.NewHire, compensation.Reason);
    }

    [Fact]
    public async Task MarkAsInitialCompanyAdminAsync_Does_Not_Duplicate_Compensation_When_One_Already_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (departmentId, locationId, employmentTypeId, positionProfileId) = await SeedMandatoryLookupsAsync(context, companyId);

        var createEmployeeHandler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(),
            new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var service = new EmployeeProvisioningService(createEmployeeHandler, context, new FakeClock(FixedUtcNow));

        var created = await service.CreateFromCandidateAsync(
            new EmployeeProvisioningRequest(
                companyId, "Ada", "Lovelace", "ada2@example.com",
                StartDate, DateOfBirth, "British", "Female",
                "EMP-0002", employmentTypeId, departmentId, locationId, positionProfileId),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        context.Compensations.Add(Compensation.Create(
            Guid.NewGuid(), companyId, created.Value, StartDate, SalaryType.Annual, 50000m, "GBP",
            null, null, "Existing", CompensationChangeReason.NewHire, created.Value, Now));
        await context.SaveChangesAsync();

        await service.MarkAsInitialCompanyAdminAsync(companyId, created.Value, CancellationToken.None);

        var compensations = await context.Compensations.Where(c => c.EmployeeId == created.Value).ToListAsync();
        Assert.Single(compensations);
        Assert.Equal(50000m, compensations[0].Salary);
    }

    [Fact]
    public async Task MarkAsInitialCompanyAdminAsync_Is_A_NoOp_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var createEmployeeHandler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(),
            new FakeProbationDateResolver(), new FakeCompanyContactValidationReader(), new FakeCompanyEmployeeNumberSettingsReader(), new FakeEmployeeNumberGenerator());
        var service = new EmployeeProvisioningService(createEmployeeHandler, context, new FakeClock(FixedUtcNow));

        await service.MarkAsInitialCompanyAdminAsync(companyId, Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(await context.Employees.ToListAsync());
        Assert.Empty(await context.Compensations.ToListAsync());
    }

    private static async Task<(Guid DepartmentId, Guid LocationId, Guid EmploymentTypeId, Guid PositionProfileId)> SeedMandatoryLookupsAsync(
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
