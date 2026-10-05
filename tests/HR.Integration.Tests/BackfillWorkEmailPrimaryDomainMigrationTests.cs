using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Migrations;
using HR.Modules.Companies.Persistence;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class BackfillWorkEmailPrimaryDomainMigrationTests
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly Dictionary<Guid, EmployeeReferenceDataSeeder.ReferenceData> _refData = new();

    public BackfillWorkEmailPrimaryDomainMigrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string Unique(string suffix = "example.com") => $"backfill-{Guid.NewGuid():N}.{suffix}";

    private async Task<Guid> SeedCompanyAsync(string? configuredDomain = null)
    {
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using var scope = _factory.Services.CreateScope();
        var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var company = Company.Create(companyId, $"Backfill-{companyId:N}", now);
        var settings = CompanySettings.CreateDefault(companyId, now);
        if (configuredDomain is not null)
            settings.UpdateWorkEmailSettings(false, configuredDomain, WorkEmailNamingConvention.FirstName, now);
        company.SetSettings(settings, now);
        companiesDb.Companies.Add(company);
        await companiesDb.SaveChangesAsync();

        return companyId;
    }

    private async Task AddEmployeeAsync(Guid companyId, string workEmail, bool isInitialAdmin = false, DateOnly? startDate = null)
    {
        if (!_refData.TryGetValue(companyId, out var refData))
        {
            refData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
            _refData[companyId] = refData;
        }

        var now = DateTimeOffset.UtcNow;
        using var scope = _factory.Services.CreateScope();
        var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, "Ada", "Lovelace", workEmail, startDate ?? new DateOnly(2026, 1, 1),
            hasSystemAccess: false, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            $"EMP-{Guid.NewGuid():N}", refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId,
            refData.PositionProfileId, now);
        if (isInitialAdmin)
            employee.MarkAsInitialCompanyAdmin(now);
        employeesDb.Employees.Add(employee);
        await employeesDb.SaveChangesAsync();
    }

    private async Task AddUserAsync(Guid companyId, string email, Guid roleId, DateTimeOffset? createdAt = null, bool active = true)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        using var scope = _factory.Services.CreateScope();
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var profile = UserProfile.Create(Guid.NewGuid(), Guid.NewGuid(), companyId, email, "Grace", "Hopper", now);
        identityDb.UserProfiles.Add(profile);
        identityDb.UserRoles.Add(UserRole.Create(profile.Id, roleId, now));
        await identityDb.SaveChangesAsync();

        if (!active)
        {
            profile.Deactivate(DateTimeOffset.UtcNow);
            await identityDb.SaveChangesAsync();
        }
    }

    private Task AddCompanyAdminUserAsync(Guid companyId, string email, DateTimeOffset? createdAt = null, bool active = true) =>
        AddUserAsync(companyId, email, SystemRoles.CompanyAdministrator, createdAt, active);

    private async Task RunBackfillAsync()
    {
        var sql = new BackfillWorkEmailPrimaryDomain().UpOperations.OfType<SqlOperation>().Single().Sql;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(string? Domain, bool Enabled, string Convention)> ReadAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var settings = await db.CompanySettings.AsNoTracking().SingleAsync(s => s.CompanyId == companyId);
        return (settings.WorkEmailPrimaryDomain, settings.WorkEmailSuggestionsEnabled, settings.WorkEmailNamingConvention.ToString());
    }

    [Fact]
    public async Task Backfill_Derives_The_Domain_From_The_Initial_Company_Admins_Work_Email()
    {
        var companyId = await SeedCompanyAsync();
        var domain = Unique();
        await AddEmployeeAsync(companyId, $"ada@{domain.ToUpperInvariant()}", isInitialAdmin: true);

        await RunBackfillAsync();

        var result = await ReadAsync(companyId);
        Assert.Equal(domain, result.Domain);
        Assert.True(result.Enabled);
        Assert.Equal("FirstNameDotLastName", result.Convention);
    }

    [Fact]
    public async Task Backfill_Prefers_The_Initial_Admin_Over_A_Company_Admin_User_And_Other_Employees()
    {
        var companyId = await SeedCompanyAsync();
        var adminDomain = Unique();
        await AddEmployeeAsync(companyId, $"ada@{adminDomain}", isInitialAdmin: true);
        await AddCompanyAdminUserAsync(companyId, $"grace@{Unique()}");
        var common = Unique();
        await AddEmployeeAsync(companyId, $"a@{common}");
        await AddEmployeeAsync(companyId, $"b@{common}");

        await RunBackfillAsync();

        Assert.Equal(adminDomain, (await ReadAsync(companyId)).Domain);
    }

    [Fact]
    public async Task Backfill_Falls_Back_To_A_Company_Admin_User_When_There_Is_No_Initial_Admin()
    {
        var companyId = await SeedCompanyAsync();
        var userDomain = Unique();
        await AddCompanyAdminUserAsync(companyId, $"Grace@{userDomain}");
        var common = Unique();
        await AddEmployeeAsync(companyId, $"a@{common}");
        await AddEmployeeAsync(companyId, $"b@{common}");

        await RunBackfillAsync();

        var result = await ReadAsync(companyId);
        Assert.Equal(userDomain, result.Domain);
        Assert.True(result.Enabled);
    }

    [Fact]
    public async Task Backfill_Falls_Back_To_A_Company_Admin_User_When_The_Initial_Admin_Is_On_A_Blocked_Domain()
    {
        var companyId = await SeedCompanyAsync();
        var userDomain = Unique();
        await AddEmployeeAsync(companyId, "ada@gmail.com", isInitialAdmin: true);
        await AddCompanyAdminUserAsync(companyId, $"grace@{userDomain}");

        await RunBackfillAsync();

        Assert.Equal(userDomain, (await ReadAsync(companyId)).Domain);
    }

    [Fact]
    public async Task Backfill_Skips_Blocked_Admin_Users_And_Picks_The_Earliest_Valid_One()
    {
        var companyId = await SeedCompanyAsync();
        var earlier = Unique();
        var later = Unique();
        var start = DateTimeOffset.UtcNow.AddDays(-10);
        await AddCompanyAdminUserAsync(companyId, "blocked@yahoo.com", start.AddDays(-5));
        await AddCompanyAdminUserAsync(companyId, $"later@{later}", start.AddDays(2));
        await AddCompanyAdminUserAsync(companyId, $"earlier@{earlier}", start);

        await RunBackfillAsync();

        Assert.Equal(earlier, (await ReadAsync(companyId)).Domain);
    }

    [Fact]
    public async Task Backfill_Ignores_Inactive_Admin_Users_And_Users_Without_The_Company_Admin_Role()
    {
        var companyId = await SeedCompanyAsync();
        await AddCompanyAdminUserAsync(companyId, $"gone@{Unique()}", active: false);
        await AddUserAsync(companyId, $"worker@{Unique()}", SystemRoles.Employee);

        await RunBackfillAsync();

        Assert.Null((await ReadAsync(companyId)).Domain);
    }

    [Fact]
    public async Task Backfill_Falls_Back_To_The_Most_Common_Employee_Domain()
    {
        var companyId = await SeedCompanyAsync();
        var common = Unique();
        var rare = Unique();
        await AddEmployeeAsync(companyId, $"a@{rare}");
        await AddEmployeeAsync(companyId, $"b@{common}");
        await AddEmployeeAsync(companyId, $"c@{common}");
        await AddEmployeeAsync(companyId, "d@gmail.com");
        await AddEmployeeAsync(companyId, "e@gmail.com");
        await AddEmployeeAsync(companyId, "f@gmail.com");

        await RunBackfillAsync();

        var result = await ReadAsync(companyId);
        Assert.Equal(common, result.Domain);
        Assert.True(result.Enabled);
    }

    [Fact]
    public async Task Backfill_Breaks_Employee_Domain_Ties_Alphabetically()
    {
        var companyId = await SeedCompanyAsync();
        var prefix = $"backfill-{Guid.NewGuid():N}";
        await AddEmployeeAsync(companyId, $"a@{prefix}-b.example.com");
        await AddEmployeeAsync(companyId, $"b@{prefix}-a.example.com");
        await AddEmployeeAsync(companyId, $"c@{prefix}-b.example.com");
        await AddEmployeeAsync(companyId, $"d@{prefix}-a.example.com");

        await RunBackfillAsync();

        Assert.Equal($"{prefix}-a.example.com", (await ReadAsync(companyId)).Domain);
    }

    [Fact]
    public async Task Backfill_Uses_The_Initial_Admin_Even_When_Other_Domains_Are_More_Common()
    {
        var companyId = await SeedCompanyAsync();
        var adminDomain = Unique();
        var common = Unique();
        await AddEmployeeAsync(companyId, $"ada@{adminDomain}", isInitialAdmin: true);
        await AddEmployeeAsync(companyId, $"a@{common}");
        await AddEmployeeAsync(companyId, $"b@{common}");

        await RunBackfillAsync();

        Assert.Equal(adminDomain, (await ReadAsync(companyId)).Domain);
    }

    [Theory]
    [InlineData("ada@gmail.com")]
    [InlineData("ada@mail.outlook.com")]
    [InlineData("ada@localhost")]
    [InlineData("not-an-email")]
    public async Task Backfill_Leaves_The_Domain_Empty_When_Every_Source_Is_Public_Or_Invalid(string email)
    {
        var companyId = await SeedCompanyAsync();
        await AddEmployeeAsync(companyId, email, isInitialAdmin: true);
        await AddCompanyAdminUserAsync(companyId, email);

        await RunBackfillAsync();

        var result = await ReadAsync(companyId);
        Assert.Null(result.Domain);
    }

    [Fact]
    public async Task Backfill_Leaves_The_Domain_Empty_When_The_Company_Has_No_Employees_Or_Users()
    {
        var companyId = await SeedCompanyAsync();

        await RunBackfillAsync();

        Assert.Null((await ReadAsync(companyId)).Domain);
    }

    [Fact]
    public async Task Backfill_Does_Not_Use_Another_Companys_Data()
    {
        var companyId = await SeedCompanyAsync();
        var otherCompanyId = await SeedCompanyAsync();
        await AddEmployeeAsync(otherCompanyId, $"ada@{Unique()}", isInitialAdmin: true);
        await AddCompanyAdminUserAsync(otherCompanyId, $"grace@{Unique()}");

        await RunBackfillAsync();

        Assert.Null((await ReadAsync(companyId)).Domain);
        Assert.NotNull((await ReadAsync(otherCompanyId)).Domain);
    }

    [Fact]
    public async Task Backfill_Does_Not_Overwrite_A_Configured_Domain_Or_Its_Settings()
    {
        var companyId = await SeedCompanyAsync("configured.example");
        await AddEmployeeAsync(companyId, $"ada@{Unique()}", isInitialAdmin: true);
        await AddCompanyAdminUserAsync(companyId, $"grace@{Unique()}");

        await RunBackfillAsync();

        var result = await ReadAsync(companyId);
        Assert.Equal("configured.example", result.Domain);
        Assert.False(result.Enabled);
        Assert.Equal("FirstName", result.Convention);
    }

    [Fact]
    public async Task Backfill_Is_Idempotent()
    {
        var companyId = await SeedCompanyAsync();
        await AddCompanyAdminUserAsync(companyId, $"grace@{Unique()}");

        await RunBackfillAsync();
        var first = await ReadAsync(companyId);
        await RunBackfillAsync();
        var second = await ReadAsync(companyId);

        Assert.NotNull(first.Domain);
        Assert.Equal(first, second);
    }
}
