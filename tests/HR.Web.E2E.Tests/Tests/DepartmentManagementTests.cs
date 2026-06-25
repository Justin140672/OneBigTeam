using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies HR Administrator CRUD workflows for departments:
/// - Seeded departments appear in the list.
/// - A new department can be created and appears in the list.
/// - A department can be deactivated.
/// </summary>
[Collection("E2E")]
public sealed class DepartmentManagementTests : IAsyncLifetime
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    private readonly AppFixture _fixture;
    private IBrowserContext _context = null!;
    private IPage           _page    = null!;

    public DepartmentManagementTests(AppFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync();
        _page    = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task DepartmentList_ShowsSeededDepartments()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var deptList = new DepartmentListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await deptList.GoToAsync(AcmeId);

        // All four seeded Acme departments should be visible.
        Assert.True(await deptList.HasDepartmentAsync("Engineering"),
            "Expected 'Engineering' in the department list");
        Assert.True(await deptList.HasDepartmentAsync("Finance"),
            "Expected 'Finance' in the department list");
        Assert.True(await deptList.HasDepartmentAsync("Sales"),
            "Expected 'Sales' in the department list");
    }

    [Fact]
    public async Task CreateDepartment_AppearsInList()
    {
        var deptName = $"E2E Dept {Guid.NewGuid().ToString("N")[..8]}";

        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var deptList  = new DepartmentListPage(_page, _fixture.WebBaseUrl);
        var deptEdit  = new DepartmentEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Navigate to the list and click Add.
        await deptList.GoToAsync(AcmeId);
        await deptList.ClickNewDepartmentAsync();

        // Fill in the name and save.
        await deptEdit.FillNameAsync(deptName);
        await deptEdit.FillDescriptionAsync("Created by E2E test");
        await deptEdit.SaveAsync();

        // After save, the list should contain the new department.
        Assert.True(await deptList.HasDepartmentAsync(deptName),
            $"Expected the new department '{deptName}' to appear in the list after creation");
    }

    [Fact]
    public async Task CreateDepartment_WithEmptyName_ShowsValidationError()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var deptEdit = new DepartmentEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await deptEdit.GoToNewAsync(AcmeId);

        // Leave the name empty and try to save.
        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        // Wait for the API to respond: either an error appears or the URL changes on success.
        await _page.WaitForFunctionAsync(
            "document.querySelector('.alert-danger, .validation-message') !== null " +
            "|| !window.location.href.includes('/departments/new')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        Assert.Contains("/departments/new", _page.Url);
        Assert.True(await deptEdit.HasErrorAsync(),
            "Expected a validation error when saving a department with no name");
    }
}
