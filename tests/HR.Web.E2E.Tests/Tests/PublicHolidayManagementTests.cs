using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies HR Administrator CRUD workflows for public holidays:
/// - Seeded public holidays appear in the list.
/// - A new holiday can be created and appears in the list.
/// - The year filter shows only the relevant year's holidays.
/// </summary>
[Collection("E2E")]
public sealed class PublicHolidayManagementTests : IAsyncLifetime
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    private readonly AppFixture _fixture;
    private IBrowserContext _context = null!;
    private IPage           _page    = null!;

    public PublicHolidayManagementTests(AppFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync();
        _page    = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task PublicHolidayList_ShowsSeeded2026Holidays()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var phList = new PublicHolidayListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await phList.GoToAsync(AcmeId);
        await phList.FilterByYearAsync(2026);

        // The seeded 2026 holidays include Christmas Day and New Year's Day.
        Assert.True(await phList.HasHolidayAsync("Christmas Day"),
            "Expected 'Christmas Day' in the 2026 public holidays list");
        Assert.True(await phList.HasHolidayAsync("New Year"),
            "Expected 'New Year' in the 2026 public holidays list");
    }

    [Fact]
    public async Task CreatePublicHoliday_AppearsInList()
    {
        var holidayName = $"E2E Holiday {Guid.NewGuid().ToString("N")[..8]}";
        // Use a date in 2027 to avoid collisions with seeded 2026 data.
        var holidayDate = "01/04/2027";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var phList  = new PublicHolidayListPage(_page, _fixture.WebBaseUrl);
        var phEdit  = new PublicHolidayEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await phList.GoToAsync(AcmeId);
        await phList.ClickNewPublicHolidayAsync();

        await phEdit.FillDateAsync(holidayDate);
        await phEdit.FillNameAsync(holidayName);
        await phEdit.FillCountryCodeAsync("GB");
        await phEdit.SaveAsync();

        // After save, filter by 2027 to see the new holiday.
        await phList.FilterByYearAsync(2027);

        Assert.True(await phList.HasHolidayAsync(holidayName),
            $"Expected the new holiday '{holidayName}' to appear in the 2027 public holidays list");
    }

    [Fact]
    public async Task CreatePublicHoliday_WithMissingFields_ShowsValidationError()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var phEdit = new PublicHolidayEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await phEdit.GoToNewAsync(AcmeId);

        // Try to save with no data filled.
        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        // Wait for the error to appear (PublicHolidayEdit validates synchronously before any API call).
        await _page.WaitForFunctionAsync(
            "document.querySelector('.alert-danger, .validation-message') !== null " +
            "|| !window.location.href.includes('/public-holidays/new')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        Assert.Contains("/public-holidays/new", _page.Url);
        Assert.True(await phEdit.HasErrorAsync(),
            "Expected validation errors when saving a public holiday with missing required fields");
    }
}
