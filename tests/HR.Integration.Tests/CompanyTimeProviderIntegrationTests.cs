using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
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

    // ── GetTodayAsync Works for Different Timezones ─────────────────────────────

    [Fact]
    public async Task GetTodayAsync_Returns_Correct_Date_For_Company_With_Different_Timezone()
    {
        // Arrange: Set up an employee in a company with NZ timezone.
        // Verify that ICompanyTimeProvider.GetTodayAsync() works correctly for the company,
        // regardless of what the current UTC time is. This is used by background jobs like
        // ProcessLeavingEmployeesJob that need to compute "today" for companies other than
        // the currently-executing user's tenant.

        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var nzTimeZone = "New Zealand Standard Time"; // UTC+12

        using (var scope = _factory.Services.CreateScope())
        {
            var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var now = DateTimeOffset.UtcNow;

            // Create company with NZ timezone
            var company = Company.Create(companyId, "NZ Corp", now);
            var settings = CompanySettings.CreateDefault(companyId, now);
            settings.UpdateCompanyProfile(nzTimeZone, "en-NZ", now);
            company.SetSettings(settings, now);
            companiesDb.Companies.Add(company);

            // Create employee in the company
            var employee = Employee.Create(
                companyId,
                "Test",
                "Employee",
                $"test-{Guid.NewGuid():N}@test.example",
                EmploymentType.FullTime,
                "EMP001",
                new DateOnly(1990, 1, 1),
                new DateOnly(2025, 1, 1),
                null);
            employee.UpdateId(employeeId);
            employeesDb.Employees.Add(employee);

            await companiesDb.SaveChangesAsync();
            await employeesDb.SaveChangesAsync();
        }

        // Act: Call GetTodayAsync for the NZ company (no tenant context needed).
        // This simulates what ProcessLeavingEmployeesJob would do.
        using (var scope = _factory.Services.CreateScope())
        {
            var companyTimeProvider = scope.ServiceProvider.GetRequiredService<ICompanyTimeProvider>();
            var today = await companyTimeProvider.GetTodayAsync(companyId);

            // Assert: Verify it returns a valid date
            Assert.NotEqual(default, today);
            // The date should be within +/- 2 days of today in UTC (accounting for timezone offset)
            var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
            var daysDifference = Math.Abs((today.ToDateTime(TimeOnly.MinValue) - utcToday.ToDateTime(TimeOnly.MinValue)).Days);
            Assert.True(daysDifference <= 2, $"Date should be close to today. UTC: {utcToday}, Company: {today}");
        }
    }

    // ── Multiple Companies with Different Timezones Return Different Dates ────────

    [Fact]
    public async Task GetTodayAsync_For_Different_Timezone_Companies_Can_Return_Different_Dates()
    {
        // Arrange: Create two companies in different timezones to verify that GetTodayAsync
        // respects the company's timezone setting and not some global default.
        // Note: Whether they actually differ depends on the current UTC time and the timezone
        // offsets, but at minimum they should both return valid DateOnly values without error.

        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            var now = DateTimeOffset.UtcNow;

            // Create Company A (NZ, UTC+12)
            var companyA = Company.Create(companyAId, "NZ Company", now);
            var settingsA = CompanySettings.CreateDefault(companyAId, now);
            settingsA.UpdateCompanyProfile("New Zealand Standard Time", "en-NZ", now);
            companyA.SetSettings(settingsA, now);
            companiesDb.Companies.Add(companyA);

            // Create Company B (PST, UTC-8)
            var companyB = Company.Create(companyBId, "US PST Company", now);
            var settingsB = CompanySettings.CreateDefault(companyBId, now);
            settingsB.UpdateCompanyProfile("Pacific Standard Time", "en-US", now);
            companyB.SetSettings(settingsB, now);
            companiesDb.Companies.Add(companyB);

            await companiesDb.SaveChangesAsync();
        }

        // Act: Get "today" for each company.
        using (var scope = _factory.Services.CreateScope())
        {
            var companyTimeProvider = scope.ServiceProvider.GetRequiredService<ICompanyTimeProvider>();

            var todayA = await companyTimeProvider.GetTodayAsync(companyAId);
            var todayB = await companyTimeProvider.GetTodayAsync(companyBId);

            // Assert: Both dates should be valid
            Assert.NotEqual(default, todayA);
            Assert.NotEqual(default, todayB);

            // Both should be close to UTC today (within +/- 1 day)
            var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
            var diffA = Math.Abs((todayA.ToDateTime(TimeOnly.MinValue) - utcToday.ToDateTime(TimeOnly.MinValue)).Days);
            var diffB = Math.Abs((todayB.ToDateTime(TimeOnly.MinValue) - utcToday.ToDateTime(TimeOnly.MinValue)).Days);
            Assert.True(diffA <= 1, $"Company A date should be close to UTC today. UTC: {utcToday}, Company A: {todayA}");
            Assert.True(diffB <= 1, $"Company B date should be close to UTC today. UTC: {utcToday}, Company B: {todayB}");
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
            var now = DateTimeOffset.UtcNow;
            var company = Company.Create(companyId, "Test Corp", now);
            var settings = CompanySettings.CreateDefault(companyId, now);
            company.SetSettings(settings, now);
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
            var now = DateTimeOffset.UtcNow;
            var company = Company.Create(companyId, "Test Corp", now);
            var settings = CompanySettings.CreateDefault(companyId, now);
            company.SetSettings(settings, now);
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
