using HR.Modules.Employees.Features.CreateEmployee;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class EmployeeProvisioningServiceTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);
    private static readonly DateOnly DateOfBirth = new(1995, 3, 20);

    [Fact]
    public async Task CreateFromCandidateAsync_Creates_Employee_And_Returns_Id()
    {
        await using var context = BuildContext();
        var createEmployeeHandler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(),
            new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());
        var service = new EmployeeProvisioningService(createEmployeeHandler);
        var companyId = Guid.NewGuid();

        var result = await service.CreateFromCandidateAsync(
            new EmployeeProvisioningRequest(
                companyId, "Emma", "Clarke", "emma.clarke@example.com",
                StartDate, DateOfBirth, "British", "Female"),
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
        var createEmployeeHandler = new CreateEmployeeHandler(
            context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(),
            new FakeProbationDateResolver(), new FakeCompanyContactValidationReader());
        var service = new EmployeeProvisioningService(createEmployeeHandler);
        var companyId = Guid.NewGuid();

        await service.CreateFromCandidateAsync(
            new EmployeeProvisioningRequest(
                companyId, "Emma", "Clarke", "emma.clarke@example.com",
                StartDate, DateOfBirth, "British", "Female"),
            CancellationToken.None);

        // Same work email in the same company should conflict.
        var result = await service.CreateFromCandidateAsync(
            new EmployeeProvisioningRequest(
                companyId, "Emma", "Clarke", "emma.clarke@example.com",
                StartDate, DateOfBirth, "British", "Female"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
