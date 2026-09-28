using HR.Modules.Companies.Contracts;
using HR.Infrastructure;

namespace HR.SharedKernel.Tests;

/// <summary>
/// Unit tests for <see cref="CompanyTimeProvider"/> covering UTC offset scenarios and timezone boundary conditions.
/// These tests verify that the service correctly converts UTC times to company-local times regardless of timezone offset,
/// and handles the boundary conditions where UTC and local dates differ.
/// </summary>
public class CompanyTimeProviderTests
{
    // ── UTC+0 (London) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTodayAsync_With_UTC_Plus_Zero_London_Timezone_Returns_Same_Date_As_UTC()
    {
        // Arrange: When UTC is 2026-01-15 14:30:00, company in London (UTC+0) should see the same date.
        var utcTime = new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("GMT Standard Time"); // UTC+0
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        // Act
        var today = await provider.GetTodayAsync(companyId);

        // Assert
        var expectedDate = DateOnly.FromDateTime(utcTime.DateTime);
        Assert.Equal(expectedDate, today);
        Assert.Equal(new DateOnly(2026, 1, 15), today);
    }

    // ── UTC+12 (New Zealand) ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetTodayAsync_With_UTC_Plus_Twelve_NZ_Timezone_When_UTC_Is_Early_Morning_Returns_Next_Day()
    {
        // Arrange: When UTC is 2026-01-01 23:00:00 (11 PM), company in NZ (UTC+12) should see 2026-01-02 (next day 11 AM).
        // Pattern: Mock IClockProvider to a UTC time that differs by timezone.
        var utcTime = new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("New Zealand Standard Time"); // UTC+12
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        // Act
        var today = await provider.GetTodayAsync(companyId);

        // Assert
        // UTC 2026-01-01 23:00:00 + 12 hours = 2026-01-02 11:00:00 in NZ
        Assert.Equal(new DateOnly(2026, 1, 2), today);
    }

    [Fact]
    public async Task GetTodayAsync_With_UTC_Plus_Twelve_NZ_Timezone_At_Midnight_UTC_Boundary()
    {
        // Arrange: When UTC is exactly 2026-01-01 00:00:00 (midnight), NZ (UTC+12) should see 2026-01-01 12:00:00.
        // This verifies the behavior at the UTC midnight boundary.
        var utcTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("New Zealand Standard Time"); // UTC+12
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        // Act
        var today = await provider.GetTodayAsync(companyId);

        // Assert
        // UTC 2026-01-01 00:00:00 + 12 hours = 2026-01-01 12:00:00 in NZ (same day)
        Assert.Equal(new DateOnly(2026, 1, 1), today);
    }

    [Fact]
    public async Task GetTodayAsync_With_UTC_Plus_Twelve_NZ_Timezone_Just_Before_Midnight_UTC()
    {
        // Arrange: When UTC is 2026-01-02 23:59:59, NZ (UTC+12) should see 2026-01-03 11:59:59.
        var utcTime = new DateTimeOffset(2026, 1, 2, 23, 59, 59, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("New Zealand Standard Time"); // UTC+12
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        // Act
        var today = await provider.GetTodayAsync(companyId);

        // Assert
        // UTC 2026-01-02 23:59:59 + 12 hours = 2026-01-03 11:59:59 in NZ
        Assert.Equal(new DateOnly(2026, 1, 3), today);
    }

    // ── UTC-8 (Pacific Standard Time) ─────────────────────────────────────────────

    [Fact]
    public async Task GetTodayAsync_With_UTC_Minus_Eight_PST_Timezone_When_UTC_Is_Morning_Returns_Previous_Day()
    {
        // Arrange: When UTC is 2026-01-02 06:00:00 (6 AM), company in PST (UTC-8) should see 2026-01-01 (previous day 10 PM).
        var utcTime = new DateTimeOffset(2026, 1, 2, 6, 0, 0, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("Pacific Standard Time"); // UTC-8
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        // Act
        var today = await provider.GetTodayAsync(companyId);

        // Assert
        // UTC 2026-01-02 06:00:00 - 8 hours = 2026-01-01 22:00:00 in PST
        Assert.Equal(new DateOnly(2026, 1, 1), today);
    }

    [Fact]
    public async Task GetTodayAsync_With_UTC_Minus_Eight_PST_Timezone_At_Midnight_UTC_Boundary()
    {
        // Arrange: When UTC is exactly 2026-01-01 00:00:00, PST (UTC-8) should see 2025-12-31 16:00:00.
        var utcTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("Pacific Standard Time"); // UTC-8
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        // Act
        var today = await provider.GetTodayAsync(companyId);

        // Assert
        // UTC 2026-01-01 00:00:00 - 8 hours = 2025-12-31 16:00:00 in PST
        Assert.Equal(new DateOnly(2025, 12, 31), today);
    }

    [Fact]
    public async Task GetTodayAsync_With_UTC_Minus_Eight_PST_Timezone_Just_After_Midnight_UTC()
    {
        // Arrange: When UTC is 2026-01-01 00:00:01 (1 second after midnight), PST should still be previous day.
        var utcTime = new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("Pacific Standard Time"); // UTC-8
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        // Act
        var today = await provider.GetTodayAsync(companyId);

        // Assert
        // UTC 2026-01-01 00:00:01 - 8 hours = 2025-12-31 16:00:01 in PST
        Assert.Equal(new DateOnly(2025, 12, 31), today);
    }

    // ── Multiple Company IDs (Background Job Scenario) ──────────────────────────────

    [Theory]
    [InlineData("GMT Standard Time")]
    [InlineData("New Zealand Standard Time")]
    [InlineData("Pacific Standard Time")]
    public async Task GetTodayAsync_Works_For_Arbitrary_CompanyIds_In_Background_Job_Scenario(string timeZoneId)
    {
        // Arrange: Verify that GetTodayAsync works without relying on ICurrentUser.TenantId.
        // This is critical for background jobs like ProcessLeavingEmployeesJob that need to compute
        // "today" for companies other than the currently-executing user's tenant.
        var utcTime = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var companyId1 = Guid.NewGuid();
        var companyId2 = Guid.NewGuid();
        var companyId3 = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader(timeZoneId);
        var currentUser = new TestCurrentUser(); // No tenant set, simulating background job context
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        // Act: Call GetTodayAsync with three distinct company IDs.
        var today1 = await provider.GetTodayAsync(companyId1);
        var today2 = await provider.GetTodayAsync(companyId2);
        var today3 = await provider.GetTodayAsync(companyId3);

        // Assert: All should return the same date since they all use the same timezone.
        Assert.Equal(today1, today2);
        Assert.Equal(today2, today3);
    }

    // ── TimeZone Property with Current User Context ────────────────────────────────

    [Fact]
    public void TimeZone_Property_When_CurrentUser_Has_TenantId_Returns_Correct_TimeZoneInfo()
    {
        // Arrange: Test the synchronous TimeZone property when current user has a tenant context.
        var companyId = Guid.NewGuid();
        var utcTime = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("New Zealand Standard Time");
        var currentUser = new TestCurrentUser(tenantId: companyId.ToString());
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        // Act
        var timeZone = provider.TimeZone;

        // Assert
        Assert.NotNull(timeZone);
        Assert.Equal("New Zealand Standard Time", timeZone.Id);
    }

    [Fact]
    public void TimeZone_Property_Throws_When_CurrentUser_Has_No_TenantId()
    {
        // Arrange: Test that the synchronous TimeZone property throws when no tenant context is available.
        var utcTime = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("GMT Standard Time");
        var currentUser = new TestCurrentUser(tenantId: null); // No tenant
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => _ = provider.TimeZone);
        Assert.Contains("No company context could be resolved", ex.Message);
    }

    // ── Today Property with Current User Context ────────────────────────────────────

    [Fact]
    public void Today_Property_When_CurrentUser_Has_TenantId_Returns_Correct_Date()
    {
        // Arrange: Test the synchronous Today property when current user has a tenant context.
        var companyId = Guid.NewGuid();
        var utcTime = new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero);
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("GMT Standard Time"); // UTC+0
        var currentUser = new TestCurrentUser(tenantId: companyId.ToString());
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        // Act
        var today = provider.Today;

        // Assert
        Assert.Equal(new DateOnly(2026, 1, 15), today);
    }

    [Fact]
    public void Today_Property_Throws_When_CurrentUser_Has_No_TenantId()
    {
        // Arrange: Test that the synchronous Today property throws when no tenant context is available.
        var utcTime = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("GMT Standard Time");
        var currentUser = new TestCurrentUser(tenantId: null); // No tenant
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => _ = provider.Today);
        Assert.Contains("No company context could be resolved", ex.Message);
    }

    [Fact]
    public void Today_Property_Throws_When_CurrentUser_TenantId_Is_Invalid_Guid()
    {
        // Arrange: Test that Today throws when TenantId is set but not a valid GUID.
        var utcTime = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("GMT Standard Time");
        var currentUser = new TestCurrentUser(tenantId: "not-a-valid-guid");
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => _ = provider.Today);
        Assert.Contains("No company context could be resolved", ex.Message);
    }

    // ── Cancellation Token (GetTodayAsync) ────────────────────────────────────────

    [Fact]
    public async Task GetTodayAsync_Respects_CancellationToken()
    {
        // Arrange
        var utcTime = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReaderWithCancellation();
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GetTodayAsync(companyId, cts.Token));
    }
}

// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
// Test Helpers and Test Doubles
// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Test double for <see cref="IClockProvider"/> that allows setting a fixed UTC time for deterministic testing.
/// </summary>
internal sealed class TestClockProvider : IClockProvider
{
    private readonly DateTimeOffset _utcNow;

    public TestClockProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public DateTimeOffset UtcNow => _utcNow;
}

/// <summary>
/// Test double for <see cref="ICurrentUser"/> that allows configuring tenant and user properties for testing.
/// </summary>
internal sealed class TestCurrentUser : ICurrentUser
{
    public TestCurrentUser(string? tenantId = null, Guid? userId = null, string? email = null)
    {
        TenantId = tenantId;
        UserId = userId;
        Email = email;
    }

    public Guid? UserId { get; }

    public string? Email { get; }

    public string? TenantId { get; }

    public bool IsAuthenticated => true;
}

/// <summary>
/// Test double for <see cref="ICompanyTimeZoneReader"/> that returns a fixed timezone.
/// </summary>
internal sealed class TestCompanyTimeZoneReader : ICompanyTimeZoneReader
{
    private readonly string _timeZoneId;

    public TestCompanyTimeZoneReader(string timeZoneId)
    {
        _timeZoneId = timeZoneId;
    }

    public Task<string> GetTimeZoneAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_timeZoneId);
    }
}

/// <summary>
/// Test double for <see cref="ICompanyTimeZoneReader"/> that can be cancelled, for testing cancellation token propagation.
/// </summary>
internal sealed class TestCompanyTimeZoneReaderWithCancellation : ICompanyTimeZoneReader
{
    public async Task<string> GetTimeZoneAsync(Guid companyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(10, cancellationToken);
        return "GMT Standard Time";
    }
}

/// <summary>
/// Fluent builder for creating test scenarios with specific UTC times and timezones.
/// Usage: new MockClockBuilder()
///     .WithUtcTime(2026, 1, 1, 23, 0, 0)
///     .Build()
/// </summary>
internal sealed class MockClockBuilder
{
    private int _year = 2026;
    private int _month = 1;
    private int _day = 15;
    private int _hour = 12;
    private int _minute = 0;
    private int _second = 0;

    public MockClockBuilder WithUtcTime(int year, int month, int day, int hour, int minute, int second)
    {
        _year = year;
        _month = month;
        _day = day;
        _hour = hour;
        _minute = minute;
        _second = second;
        return this;
    }

    public DateTimeOffset Build()
    {
        return new DateTimeOffset(_year, _month, _day, _hour, _minute, _second, TimeSpan.Zero);
    }
}

/// <summary>
/// Constants for test timezones used in boundary testing.
/// </summary>
internal static class TimeZoneTestData
{
    public const string UtcZero = "GMT Standard Time";
    public const string UtcPlus12 = "New Zealand Standard Time";
    public const string UtcMinus8 = "Pacific Standard Time";
    public const string UtcPlus5_30 = "India Standard Time"; // For additional boundary testing if needed
}
