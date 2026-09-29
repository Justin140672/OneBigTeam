using HR.Modules.Companies.Contracts;
using HR.Infrastructure;

namespace HR.SharedKernel.Tests;

public class CompanyTimeProviderTests
{

    [Fact]
    public async Task GetTodayAsync_With_UTC_Plus_Zero_London_Timezone_Returns_Same_Date_As_UTC()
    {
        var utcTime = new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("GMT Standard Time");
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        var today = await provider.GetTodayAsync(companyId);

        var expectedDate = DateOnly.FromDateTime(utcTime.DateTime);
        Assert.Equal(expectedDate, today);
        Assert.Equal(new DateOnly(2026, 1, 15), today);
    }


    [Fact]
    public async Task GetTodayAsync_With_UTC_Plus_Twelve_NZ_Timezone_When_UTC_Is_Early_Morning_Returns_Next_Day()
    {
        var utcTime = new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("New Zealand Standard Time");
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        var today = await provider.GetTodayAsync(companyId);

        Assert.Equal(new DateOnly(2026, 1, 2), today);
    }

    [Fact]
    public async Task GetTodayAsync_With_UTC_Plus_Twelve_NZ_Timezone_At_Midnight_UTC_Boundary()
    {
        var utcTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("New Zealand Standard Time");
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        var today = await provider.GetTodayAsync(companyId);

        Assert.Equal(new DateOnly(2026, 1, 1), today);
    }

    [Fact]
    public async Task GetTodayAsync_With_UTC_Plus_Twelve_NZ_Timezone_Just_Before_Midnight_UTC()
    {
        var utcTime = new DateTimeOffset(2026, 1, 2, 23, 59, 59, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("New Zealand Standard Time");
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        var today = await provider.GetTodayAsync(companyId);

        Assert.Equal(new DateOnly(2026, 1, 3), today);
    }


    [Fact]
    public async Task GetTodayAsync_With_UTC_Minus_Eight_PST_Timezone_When_UTC_Is_Morning_Returns_Previous_Day()
    {
        var utcTime = new DateTimeOffset(2026, 1, 2, 6, 0, 0, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("Pacific Standard Time");
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        var today = await provider.GetTodayAsync(companyId);

        Assert.Equal(new DateOnly(2026, 1, 1), today);
    }

    [Fact]
    public async Task GetTodayAsync_With_UTC_Minus_Eight_PST_Timezone_At_Midnight_UTC_Boundary()
    {
        var utcTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("Pacific Standard Time");
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        var today = await provider.GetTodayAsync(companyId);

        Assert.Equal(new DateOnly(2025, 12, 31), today);
    }

    [Fact]
    public async Task GetTodayAsync_With_UTC_Minus_Eight_PST_Timezone_Just_After_Midnight_UTC()
    {
        var utcTime = new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("Pacific Standard Time");
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        var today = await provider.GetTodayAsync(companyId);

        Assert.Equal(new DateOnly(2025, 12, 31), today);
    }


    [Theory]
    [InlineData("GMT Standard Time")]
    [InlineData("New Zealand Standard Time")]
    [InlineData("Pacific Standard Time")]
    public async Task GetTodayAsync_Works_For_Arbitrary_CompanyIds_In_Background_Job_Scenario(string timeZoneId)
    {
        var utcTime = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var companyId1 = Guid.NewGuid();
        var companyId2 = Guid.NewGuid();
        var companyId3 = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader(timeZoneId);
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        var today1 = await provider.GetTodayAsync(companyId1);
        var today2 = await provider.GetTodayAsync(companyId2);
        var today3 = await provider.GetTodayAsync(companyId3);

        Assert.Equal(today1, today2);
        Assert.Equal(today2, today3);
    }


    [Fact]
    public void TimeZone_Property_When_CurrentUser_Has_TenantId_Returns_Correct_TimeZoneInfo()
    {
        var companyId = Guid.NewGuid();
        var utcTime = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("New Zealand Standard Time");
        var currentUser = new TestCurrentUser(tenantId: companyId.ToString());
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        var timeZone = provider.TimeZone;

        Assert.NotNull(timeZone);
        Assert.Equal("New Zealand Standard Time", timeZone.Id);
    }

    [Fact]
    public void TimeZone_Property_Throws_When_CurrentUser_Has_No_TenantId()
    {
        var utcTime = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("GMT Standard Time");
        var currentUser = new TestCurrentUser(tenantId: null);
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        var ex = Assert.Throws<InvalidOperationException>(() => _ = provider.TimeZone);
        Assert.Contains("No company context could be resolved", ex.Message);
    }


    [Fact]
    public void Today_Property_When_CurrentUser_Has_TenantId_Returns_Correct_Date()
    {
        var companyId = Guid.NewGuid();
        var utcTime = new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero);
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("GMT Standard Time");
        var currentUser = new TestCurrentUser(tenantId: companyId.ToString());
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        var today = provider.Today;

        Assert.Equal(new DateOnly(2026, 1, 15), today);
    }

    [Fact]
    public void Today_Property_Throws_When_CurrentUser_Has_No_TenantId()
    {
        var utcTime = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("GMT Standard Time");
        var currentUser = new TestCurrentUser(tenantId: null);
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        var ex = Assert.Throws<InvalidOperationException>(() => _ = provider.Today);
        Assert.Contains("No company context could be resolved", ex.Message);
    }

    [Fact]
    public void Today_Property_Throws_When_CurrentUser_TenantId_Is_Invalid_Guid()
    {
        var utcTime = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReader("GMT Standard Time");
        var currentUser = new TestCurrentUser(tenantId: "not-a-valid-guid");
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);

        var ex = Assert.Throws<InvalidOperationException>(() => _ = provider.Today);
        Assert.Contains("No company context could be resolved", ex.Message);
    }


    [Fact]
    public async Task GetTodayAsync_Respects_CancellationToken()
    {
        var utcTime = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var clockProvider = new TestClockProvider(utcTime);
        var timeZoneReader = new TestCompanyTimeZoneReaderWithCancellation();
        var currentUser = new TestCurrentUser();
        var provider = new CompanyTimeProvider(currentUser, timeZoneReader, clockProvider);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GetTodayAsync(companyId, cts.Token));
    }
}


internal sealed class TestClockProvider : IClockProvider
{
    private readonly DateTimeOffset _utcNow;

    public TestClockProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public DateTimeOffset UtcNow => _utcNow;
}

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

internal sealed class TestCompanyTimeZoneReaderWithCancellation : ICompanyTimeZoneReader
{
    public async Task<string> GetTimeZoneAsync(Guid companyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(10, cancellationToken);
        return "GMT Standard Time";
    }
}

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

internal static class TimeZoneTestData
{
    public const string UtcZero = "GMT Standard Time";
    public const string UtcPlus12 = "New Zealand Standard Time";
    public const string UtcMinus8 = "Pacific Standard Time";
    public const string UtcPlus5_30 = "India Standard Time";
}
