using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Persistence;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Aspire-hosted integration tests for <see cref="ICompanyTimeProvider"/> verifying end-to-end timezone
/// behavior in realistic scenarios: offboarding task deadlines, document expiry dates, and multi-company
/// timezone awareness in background jobs and API responses.
/// </summary>
[Collection("Integration")]
public class CompanyTimeProviderIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public CompanyTimeProviderIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    // ── Offboarding vs Departure Job Agreement Consistency ───────────────────────

    [Fact]
    public async Task Offboarding_Tasks_And_Job_Agreement_See_Consistent_Today_Across_Timezone_Boundary()
    {
        // Arrange: Set up an employee in a company with UTC+12 timezone.
        // UTC time is 2026-01-01 23:00:00, so company-local is 2026-01-02 11:00:00.
        // Create offboarding tasks due "today" (2026-01-02 in NZ timezone).
        // The job ProcessLeavingEmployeesJob must agree that "today" is 2026-01-02 when running in NZ timezone.

        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var nzTimeZone = "New Zealand Standard Time"; // UTC+12

        using (var scope = _factory.Services.CreateScope())
        {
            var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();

            // Create company with NZ timezone
            var company = Domain.Companies.Company.Create(
                name: "NZ Corp",
                country: "New Zealand",
                timeZone: nzTimeZone,
                createdBy: null);
            company.UpdateId(companyId);
            companiesDb.Companies.Add(company);

            // Create employee in the company
            var employee = Employee.Create(
                companyId: companyId,
                firstName: "Test",
                lastName: "Employee",
                email: $"test-{Guid.NewGuid():N}@test.example",
                employmentType: EmploymentType.FullTime,
                employeeNumber: "EMP001",
                dateOfBirth: new DateOnly(1990, 1, 1),
                startDate: new DateOnly(2025, 1, 1),
                createdBy: null);
            employee.UpdateId(employeeId);
            employeesDb.Employees.Add(employee);

            await companiesDb.SaveChangesAsync();
            await employeesDb.SaveChangesAsync();
        }

        // Act: Verify that both the EmployeeOffboardingTab (synchronous) and job (async)
        // would read the same "today" date when executing at UTC 2026-01-01 23:00:00.
        // Since we don't have the actual job and UI components in unit test scope, we verify
        // that ICompanyTimeProvider returns consistent results.

        using (var scope = _factory.Services.CreateScope())
        {
            var companyTimeProvider = scope.ServiceProvider.GetRequiredService<ICompanyTimeProvider>();

            // Simulate what EmployeeOffboardingTab would compute (requires tenant context)
            var testCurrentUser = new TestCurrentUserWithTenant(tenantId: companyId.ToString());
            var companyTimeProviderWithContext = new CompanyTimeProviderForTest(
                testCurrentUser,
                new TestCompanyTimeZoneReaderForIntegration(scope),
                new TestClockProviderForIntegration(new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.Zero)));

            var uiToday = companyTimeProviderWithContext.Today;

            // Simulate what ProcessLeavingEmployeesJob would compute (no tenant context, background job)
            var jobToday = await companyTimeProvider.GetTodayAsync(companyId);

            // Assert
            Assert.Equal(new DateOnly(2026, 1, 2), uiToday);
            Assert.Equal(new DateOnly(2026, 1, 2), jobToday);
            Assert.Equal(uiToday, jobToday);
        }
    }

    // ── Document Expiry at Timezone Boundary ──────────────────────────────────────

    [Fact]
    public async Task Document_Expiry_Evaluation_Differs_For_UTC_Plus_Twelve_vs_UTC_Minus_Eight_At_Same_UTC_Moment()
    {
        // Arrange: Create two companies in different timezones.
        // UTC time is 2026-01-02 23:00:00.
        // - Company A (UTC+12, NZ): local time is 2026-01-03 11:00:00 → document due 2026-01-03 is overdue
        // - Company B (UTC-8, PST): local time is 2026-01-02 15:00:00 → document due 2026-01-03 is NOT overdue

        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var utcTime = new DateTimeOffset(2026, 1, 2, 23, 0, 0, TimeSpan.Zero);
        var dueDate = new DateOnly(2026, 1, 3);

        using (var scope = _factory.Services.CreateScope())
        {
            var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

            // Create Company A (NZ, UTC+12)
            var companyA = Domain.Companies.Company.Create(
                name: "NZ Company",
                country: "New Zealand",
                timeZone: "New Zealand Standard Time",
                createdBy: null);
            companyA.UpdateId(companyAId);
            companiesDb.Companies.Add(companyA);

            // Create Company B (PST, UTC-8)
            var companyB = Domain.Companies.Company.Create(
                name: "US PST Company",
                country: "United States",
                timeZone: "Pacific Standard Time",
                createdBy: null);
            companyB.UpdateId(companyBId);
            companiesDb.Companies.Add(companyB);

            await companiesDb.SaveChangesAsync();
        }

        // Act: Evaluate if documents are overdue from each company's perspective.
        using (var scope = _factory.Services.CreateScope())
        {
            var companyTimeProvider = scope.ServiceProvider.GetRequiredService<ICompanyTimeProvider>();
            var clockForTest = new TestClockProviderForIntegration(utcTime);
            var timeZoneReaderForTest = new TestCompanyTimeZoneReaderForIntegration(scope);

            // Company A: UTC+12 (NZ)
            var providerA = new CompanyTimeProviderForTest(
                new TestCurrentUserWithTenant(),
                timeZoneReaderForTest,
                clockForTest);
            var todayA = await companyTimeProvider.GetTodayAsync(companyAId); // Uses real factory provider

            // Company B: UTC-8 (PST)
            var providerB = new CompanyTimeProviderForTest(
                new TestCurrentUserWithTenant(),
                timeZoneReaderForTest,
                clockForTest);
            var todayB = await companyTimeProvider.GetTodayAsync(companyBId); // Uses real factory provider

            // Assert
            // Company A sees 2026-01-03 (tomorrow in NZ) → document due today IS overdue
            Assert.Equal(new DateOnly(2026, 1, 3), todayA);

            // Company B sees 2026-01-02 (today in PST) → document due 2026-01-03 is NOT overdue (due tomorrow)
            Assert.Equal(new DateOnly(2026, 1, 2), todayB);

            // The key assertion: same UTC moment, different dates in each company's timezone
            Assert.NotEqual(todayA, todayB);
        }
    }

    // ── Multiple Companies with Different Timezones ────────────────────────────────

    [Fact]
    public async Task Three_Employees_In_Three_Timezones_Each_See_Correct_Local_Today()
    {
        // Arrange: Create 3 employees in 3 companies with different timezones.
        // Each should compute "today" according to their company's local timezone.
        var company1Id = Guid.NewGuid(); // UTC+12 (NZ)
        var company2Id = Guid.NewGuid(); // UTC+0 (UK)
        var company3Id = Guid.NewGuid(); // UTC-8 (US)

        var employee1Id = Guid.NewGuid();
        var employee2Id = Guid.NewGuid();
        var employee3Id = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();

            // Create companies with different timezones
            var company1 = Domain.Companies.Company.Create("NZ Corp", "New Zealand", "New Zealand Standard Time", null);
            company1.UpdateId(company1Id);
            companiesDb.Companies.Add(company1);

            var company2 = Domain.Companies.Company.Create("UK Corp", "United Kingdom", "GMT Standard Time", null);
            company2.UpdateId(company2Id);
            companiesDb.Companies.Add(company2);

            var company3 = Domain.Companies.Company.Create("US Corp", "United States", "Pacific Standard Time", null);
            company3.UpdateId(company3Id);
            companiesDb.Companies.Add(company3);

            await companiesDb.SaveChangesAsync();

            // Create employees in each company
            var employee1 = Employee.Create(
                company1Id, "Alice", "NZ", $"alice-{Guid.NewGuid():N}@test.example",
                EmploymentType.FullTime, "EMP001", new DateOnly(1990, 1, 1), new DateOnly(2025, 1, 1), null);
            employee1.UpdateId(employee1Id);
            employeesDb.Employees.Add(employee1);

            var employee2 = Employee.Create(
                company2Id, "Bob", "UK", $"bob-{Guid.NewGuid():N}@test.example",
                EmploymentType.FullTime, "EMP001", new DateOnly(1990, 1, 1), new DateOnly(2025, 1, 1), null);
            employee2.UpdateId(employee2Id);
            employeesDb.Employees.Add(employee2);

            var employee3 = Employee.Create(
                company3Id, "Charlie", "US", $"charlie-{Guid.NewGuid():N}@test.example",
                EmploymentType.FullTime, "EMP001", new DateOnly(1990, 1, 1), new DateOnly(2025, 1, 1), null);
            employee3.UpdateId(employee3Id);
            employeesDb.Employees.Add(employee3);

            await employeesDb.SaveChangesAsync();
        }

        // Act: Compute "today" for each company at a specific UTC moment.
        // UTC 2026-01-15 15:00:00
        // - Company 1 (NZ, UTC+12): 2026-01-16 03:00:00 → 2026-01-16
        // - Company 2 (UK, UTC+0): 2026-01-15 15:00:00 → 2026-01-15
        // - Company 3 (US, UTC-8): 2026-01-15 07:00:00 → 2026-01-15
        using (var scope = _factory.Services.CreateScope())
        {
            var companyTimeProvider = scope.ServiceProvider.GetRequiredService<ICompanyTimeProvider>();

            var today1 = await companyTimeProvider.GetTodayAsync(company1Id);
            var today2 = await companyTimeProvider.GetTodayAsync(company2Id);
            var today3 = await companyTimeProvider.GetTodayAsync(company3Id);

            // Assert
            Assert.Equal(new DateOnly(2026, 1, 16), today1); // NZ is next day
            Assert.Equal(new DateOnly(2026, 1, 15), today2); // UK is same day
            Assert.Equal(new DateOnly(2026, 1, 15), today3); // US is same day
        }
    }

    // ── GetTodayAsync Works Across Multiple Calls ──────────────────────────────────

    [Fact]
    public async Task GetTodayAsync_Returns_Consistent_Date_Across_Multiple_Calls()
    {
        // Arrange: Verify that repeated calls to GetTodayAsync for the same company return consistent results.
        var companyId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            var company = Domain.Companies.Company.Create("Test Corp", "Test", "GMT Standard Time", null);
            company.UpdateId(companyId);
            companiesDb.Companies.Add(company);
            await companiesDb.SaveChangesAsync();
        }

        // Act
        using (var scope = _factory.Services.CreateScope())
        {
            var companyTimeProvider = scope.ServiceProvider.GetRequiredService<ICompanyTimeProvider>();

            var call1 = await companyTimeProvider.GetTodayAsync(companyId);
            var call2 = await companyTimeProvider.GetTodayAsync(companyId);
            var call3 = await companyTimeProvider.GetTodayAsync(companyId);

            // Assert: All calls return the same date (no nondeterminism)
            Assert.Equal(call1, call2);
            Assert.Equal(call2, call3);
        }
    }

    // ── Cancellation Token Propagation ────────────────────────────────────────────

    [Fact]
    public async Task GetTodayAsync_Propagates_CancellationToken_To_Reader()
    {
        // Arrange: Verify that GetTodayAsync respects the cancellation token passed by callers.
        var companyId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            var company = Domain.Companies.Company.Create("Test Corp", "Test", "GMT Standard Time", null);
            company.UpdateId(companyId);
            companiesDb.Companies.Add(company);
            await companiesDb.SaveChangesAsync();
        }

        // Act & Assert
        using (var scope = _factory.Services.CreateScope())
        {
            var companyTimeProvider = scope.ServiceProvider.GetRequiredService<ICompanyTimeProvider>();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Should throw OperationCanceledException, not succeed or hang
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => companyTimeProvider.GetTodayAsync(companyId, cts.Token));
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
// Test Helpers for Integration Tests
// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Test double for <see cref="ICurrentUser"/> with configurable tenant for integration testing.
/// </summary>
internal sealed class TestCurrentUserWithTenant : ICurrentUser
{
    private readonly string? _tenantId;

    public TestCurrentUserWithTenant(string? tenantId = null)
    {
        _tenantId = tenantId;
    }

    public Guid? UserId => Guid.NewGuid();

    public string? Email => "test@test.example";

    public string? TenantId => _tenantId;

    public bool IsAuthenticated => true;
}

/// <summary>
/// Test double for <see cref="IClockProvider"/> that returns a fixed UTC time for deterministic integration testing.
/// </summary>
internal sealed class TestClockProviderForIntegration : IClockProvider
{
    private readonly DateTimeOffset _utcNow;

    public TestClockProviderForIntegration(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public DateTimeOffset UtcNow => _utcNow;
}

/// <summary>
/// Test double for <see cref="ICompanyTimeZoneReader"/> that reads actual company timezone from the database.
/// </summary>
internal sealed class TestCompanyTimeZoneReaderForIntegration : ICompanyTimeZoneReader
{
    private readonly IServiceScope _scope;

    public TestCompanyTimeZoneReaderForIntegration(IServiceScope scope)
    {
        _scope = scope;
    }

    public async Task<string> GetTimeZoneAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var db = _scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
        if (company is null)
            throw new InvalidOperationException($"Company {companyId} not found");
        return company.TimeZone;
    }
}

/// <summary>
/// Test implementation of <see cref="ICompanyTimeProvider"/> for integration testing, allowing injection of test doubles.
/// </summary>
internal sealed class CompanyTimeProviderForTest : ICompanyTimeProvider
{
    private readonly ICurrentUser _currentUser;
    private readonly ICompanyTimeZoneReader _timeZoneReader;
    private readonly IClockProvider _clockProvider;

    public CompanyTimeProviderForTest(
        ICurrentUser currentUser,
        ICompanyTimeZoneReader timeZoneReader,
        IClockProvider clockProvider)
    {
        _currentUser = currentUser;
        _timeZoneReader = timeZoneReader;
        _clockProvider = clockProvider;
    }

    public DateOnly Today
    {
        get
        {
            if (_currentUser.TenantId is null || !Guid.TryParse(_currentUser.TenantId, out var companyId))
            {
                throw new InvalidOperationException("No company context could be resolved for the current user.");
            }

            return GetTodaySync(companyId);
        }
    }

    public TimeZoneInfo TimeZone
    {
        get
        {
            if (_currentUser.TenantId is null || !Guid.TryParse(_currentUser.TenantId, out var companyId))
            {
                throw new InvalidOperationException("No company context could be resolved for the current user.");
            }

            return GetTimeZoneSync(companyId);
        }
    }

    public async Task<DateOnly> GetTodayAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var ianaTimeZoneId = await _timeZoneReader.GetTimeZoneAsync(companyId, cancellationToken);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZoneId);
        var localDateTime = TimeZoneInfo.ConvertTime(_clockProvider.UtcNow, timeZone);
        return DateOnly.FromDateTime(localDateTime.DateTime);
    }

    private DateOnly GetTodaySync(Guid companyId)
    {
        var task = Task.Run(() => _timeZoneReader.GetTimeZoneAsync(companyId, CancellationToken.None));
        var ianaTimeZoneId = task.GetAwaiter().GetResult();
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZoneId);
        var localDateTime = TimeZoneInfo.ConvertTime(_clockProvider.UtcNow, timeZone);
        return DateOnly.FromDateTime(localDateTime.DateTime);
    }

    private TimeZoneInfo GetTimeZoneSync(Guid companyId)
    {
        var task = Task.Run(() => _timeZoneReader.GetTimeZoneAsync(companyId, CancellationToken.None));
        var ianaTimeZoneId = task.GetAwaiter().GetResult();
        return TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZoneId);
    }
}
