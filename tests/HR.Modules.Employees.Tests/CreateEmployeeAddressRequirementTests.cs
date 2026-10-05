using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateEmployee;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class CreateEmployeeAddressRequirementTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    [Theory]
    [InlineData(nameof(CreateEmployeeRequest.AddressLine1), null)]
    [InlineData(nameof(CreateEmployeeRequest.AddressLine1), "")]
    [InlineData(nameof(CreateEmployeeRequest.AddressLine1), "   ")]
    [InlineData(nameof(CreateEmployeeRequest.City), null)]
    [InlineData(nameof(CreateEmployeeRequest.City), "   ")]
    [InlineData(nameof(CreateEmployeeRequest.PostCode), null)]
    [InlineData(nameof(CreateEmployeeRequest.PostCode), "   ")]
    public async Task Handler_Rejects_Missing_Or_Whitespace_Required_Address_Fields(string property, string? value)
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var lookups = await SeedAsync(context, companyId);

        var request = Apply(BuildRequest(companyId, lookups), property, value);

        var result = await BuildHandler(context).HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(context.Employees);
    }

    [Fact]
    public async Task Handler_Rejects_Postcode_That_Does_Not_Match_The_Company_Rule()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var lookups = await SeedAsync(context, companyId);

        var result = await BuildHandler(context, ukRules: true).HandleAsync(
            BuildRequest(companyId, lookups) with { PostCode = "not a postcode" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("postcode", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handler_Persists_Trimmed_Address_And_Leaves_Optional_Fields_Null()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var lookups = await SeedAsync(context, companyId);

        var result = await BuildHandler(context, ukRules: true).HandleAsync(
            BuildRequest(companyId, lookups) with
            {
                AddressLine1 = "  1 High Street  ",
                City = " London ",
                PostCode = " SW1A 1AA ",
                AddressLine2 = "   ",
                County = null,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await context.Employees.SingleAsync();
        Assert.Equal("1 High Street", saved.AddressLine1);
        Assert.Equal("London", saved.City);
        Assert.Equal("SW1A 1AA", saved.PostCode);
        Assert.Null(saved.AddressLine2);
        Assert.Null(saved.County);
    }

    [Fact]
    public async Task Handler_Does_Not_Require_Address_For_The_Initial_Company_Admin()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var lookups = await SeedAsync(context, companyId);

        var result = await BuildHandler(context).HandleAsync(
            BuildRequest(companyId, lookups) with
            {
                AddressLine1 = null,
                City = null,
                PostCode = null,
                IsInitialCompanyAdmin = true,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null((await context.Employees.SingleAsync()).AddressLine1);
    }

    [Theory]
    [InlineData(nameof(CreateEmployeeRequest.AddressLine1), null)]
    [InlineData(nameof(CreateEmployeeRequest.AddressLine1), "   ")]
    [InlineData(nameof(CreateEmployeeRequest.City), "")]
    [InlineData(nameof(CreateEmployeeRequest.City), "   ")]
    [InlineData(nameof(CreateEmployeeRequest.PostCode), null)]
    [InlineData(nameof(CreateEmployeeRequest.PostCode), "   ")]
    public void Validator_Rejects_Missing_Or_Whitespace_Required_Address_Fields(string property, string? value)
    {
        var lookups = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var request = Apply(BuildRequest(Guid.NewGuid(), lookups), property, value);

        var result = new CreateEmployeeValidator().Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == property);
    }

    [Fact]
    public void Validator_Accepts_A_Complete_Address_With_Optional_Fields_Blank()
    {
        var lookups = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var request = BuildRequest(Guid.NewGuid(), lookups) with { AddressLine2 = null, County = null, Country = null };

        var result = new CreateEmployeeValidator().Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_Rejects_Address_Fields_Over_Max_Length()
    {
        var lookups = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var request = BuildRequest(Guid.NewGuid(), lookups) with
        {
            AddressLine1 = new string('a', 201),
            City = new string('b', 101),
            PostCode = new string('c', 21),
        };

        var result = new CreateEmployeeValidator().Validate(request);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.AddressLine1));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.City));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEmployeeRequest.PostCode));
    }

    private static CreateEmployeeRequest Apply(CreateEmployeeRequest request, string property, string? value) => property switch
    {
        nameof(CreateEmployeeRequest.AddressLine1) => request with { AddressLine1 = value },
        nameof(CreateEmployeeRequest.City) => request with { City = value },
        _ => request with { PostCode = value },
    };

    private static CreateEmployeeRequest BuildRequest(
        Guid companyId, (Guid DepartmentId, Guid LocationId, Guid EmploymentTypeId, Guid PositionProfileId) lookups) => new()
    {
        CompanyId = companyId,
        DepartmentId = lookups.DepartmentId,
        LocationId = lookups.LocationId,
        PositionProfileId = lookups.PositionProfileId,
        EmploymentTypeId = lookups.EmploymentTypeId,
        EmployeeNumber = "EMP-0001",
        FirstName = "Alice",
        LastName = "Smith",
        WorkEmail = "alice.smith@example.com",
        StartDate = new DateOnly(2026, 7, 1),
        DateOfBirth = new DateOnly(1990, 5, 20),
        Nationality = "British",
        Gender = "Female",
        AddressLine1 = "1 High Street",
        City = "London",
        PostCode = "SW1A 1AA",
        Salary = 50000m,
        SalaryFrequency = "Annual",
        Currency = "GBP",
    };

    private static CreateEmployeeHandler BuildHandler(EmployeesDbContext context, bool ukRules = false) =>
        new(
            context,
            new FakeClock(FixedUtcNow),
            new FakeProbationDateResolver(),
            ukRules
                ? new FakeCompanyContactValidationReader(UkTestRegexPatterns.Postcode, UkTestRegexPatterns.Telephone, UkTestRegexPatterns.Mobile)
                : new FakeCompanyContactValidationReader(),
            new FakeCompanyEmployeeNumberSettingsReader(),
            new FakeEmployeeNumberGenerator());

    private static async Task<(Guid DepartmentId, Guid LocationId, Guid EmploymentTypeId, Guid PositionProfileId)> SeedAsync(
        EmployeesDbContext context, Guid companyId)
    {
        var department = Department.Create(Guid.NewGuid(), companyId, "Engineering", null, Now);
        var locationType = LocationType.Create(Guid.NewGuid(), companyId, "Office", null, Now);
        var location = Location.Create(Guid.NewGuid(), companyId, locationType.Id, "Head Office", null, Now);
        var positionProfile = PositionProfile.Create(
            Guid.NewGuid(), companyId, department.Id, location.Id, "Developer",
            null, null, null, null, null, null, Guid.NewGuid(), Now);
        var employmentType = EmploymentType.Create(Guid.NewGuid(), companyId, "Permanent", null, Now);

        context.Departments.Add(department);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        context.PositionProfiles.Add(positionProfile);
        context.EmploymentTypes.Add(employmentType);
        await context.SaveChangesAsync();

        return (department.Id, location.Id, employmentType.Id, positionProfile.Id);
    }

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
