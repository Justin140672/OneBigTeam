using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Fixture for the "HrSettingsSerial" collection — tests that read AND write the single shared
/// company-level HR/company settings row for the Acme tenant (CompanySettings: working days,
/// employee-number prefix/mode, probation months, TimeZone/Locale, default acknowledgement
/// statement, DisplaySalaryOnEmployeeProfile, etc. — see HR.Modules.Companies.Domain.CompanySettings).
/// Concurrent test classes mutating that one row would race and produce flaky/wrong assertions, so
/// every test class here runs sequentially relative to the others in this collection (but still in
/// parallel with the unrelated role-fixed classes that don't touch this row).
///
/// This is a distinct collection from "CrossUser" — that one is reserved for tests that switch
/// persona mid-test via the dev persona switcher; mixing the two concerns would make each
/// collection's purpose unclear. Most tests here use a single persona throughout (frequently logging
/// back in as the same one, or briefly as a second persona to toggle an HR-admin-only setting), so
/// no storageState pre-authentication is used here either — same reasoning and shape as
/// CrossUserFixture.
/// </summary>
public sealed class HrSettingsSerialFixture : IAsyncLifetime, IPersonaFixture
{
    private AppFixture? _app;

    public string WebBaseUrl => _app!.WebBaseUrl;
    public string MarketingBaseUrl => _app!.MarketingBaseUrl;
    public string ApiBaseUrl => _app!.ApiBaseUrl;
    public string AdminWebBaseUrl => _app!.AdminWebBaseUrl;
    public IBrowser Browser => _app!.Browser;
    public BrowserNewContextOptions? AuthenticatedContextOptions => null;
    public bool RequiresFullTeardownDelay => true;

    public async Task InitializeAsync() => _app = await SharedAppFixture.AcquireAsync();

    public async Task DisposeAsync() => await SharedAppFixture.ReleaseAsync();
}
