using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that an HR Administrator can create a new employee and that
/// the employee appears in the employee list afterwards.
/// </summary>
[Collection("E2E")]
public sealed class CreateEmployeeTests : IAsyncLifetime
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    private readonly AppFixture _fixture;
    private IBrowserContext _context = null!;
    private IPage           _page    = null!;

    public CreateEmployeeTests(AppFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync();
        _page    = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task CreateEmployee_WithRequiredFields_AppearsInEmployeeList()
    {
        // Use a unique email so the test can be run more than once on the same database.
        var unique     = Guid.NewGuid().ToString("N")[..8];
        var firstName  = "E2E";
        var lastName   = $"Emp{unique}";
        var workEmail  = $"e2e.emp{unique}@acme.example";
        var startDate  = "01/03/2026";
        var dob        = "15/06/1990";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList  = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Laura (HR Administrator) ─────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // ── Step 2: Navigate to the employee list ─────────────────────────────
        await empList.GoToAsync(AcmeId);

        // ── Step 3: Click "Add" to navigate to the new-employee form ──────────
        await empList.ClickNewEmployeeAsync();

        // ── Step 4: Fill in required personal information ─────────────────────
        await empEdit.FillFirstNameAsync(firstName);
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);

        // Gender is required — select the first option.
        await empEdit.SelectDropdownAsync("Gender", "Male");

        // Nationality is required.
        await empEdit.SelectDropdownAsync("Nationality", "British");

        // Date of birth.
        await empEdit.FillDateOfBirthAsync(dob);

        // Start date.
        await empEdit.FillStartDateAsync(startDate);

        // ── Step 5: Save the new employee ─────────────────────────────────────
        await empEdit.SaveNewEmployeeAsync();

        // ── Step 6: Back on the employee list — new employee should be present ─
        Assert.True(await empList.HasEmployeeAsync(lastName),
            $"Expected the new employee '{lastName}' to appear in the employee list after creation");
    }

    [Fact]
    public async Task CreateEmployee_WithMissingRequiredFields_ShowsValidationErrors()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToNewAsync(AcmeId);

        // Attempt to save without filling anything — should show validation errors.
        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        // Wait for the async save to complete: either an error banner appears or the URL changes.
        await _page.WaitForFunctionAsync(
            "document.querySelector('.alert-danger, .validation-message') !== null " +
            "|| !window.location.href.includes('/employees/new')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        // Page must stay on the new-employee form (URL still ends with /new).
        Assert.Contains("/employees/new", _page.Url);

        Assert.True(await empEdit.HasErrorAsync(),
            "Expected validation errors to appear when saving an empty employee form");
    }
}
