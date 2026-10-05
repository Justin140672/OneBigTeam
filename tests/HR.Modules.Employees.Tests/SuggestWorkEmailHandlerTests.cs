using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.SuggestWorkEmail;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class SuggestWorkEmailHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static CompanyWorkEmailSettings Enabled(
        string? primary = "example.com",
        string[]? additional = null,
        WorkEmailNamingConvention convention = WorkEmailNamingConvention.FirstNameDotLastName) =>
        new(true, primary, additional ?? [], convention);

    private static SuggestWorkEmailHandler BuildHandler(EmployeesDbContext context, CompanyWorkEmailSettings? settings) =>
        new(context, new FakeCompanyWorkEmailSettingsReader(settings));

    private static SuggestWorkEmailRequest Request(
        Guid companyId, string? first = "Jane", string? last = "Smith", string? domain = null) =>
        new() { CompanyId = companyId, FirstName = first, LastName = last, Domain = domain };

    private static async Task AddEmployeeAsync(EmployeesDbContext context, Guid companyId, string workEmail, string number = "EMP-0001")
    {
        context.Employees.Add(Employee.Create(
            Guid.NewGuid(), companyId, "Existing", "User", workEmail, StartDate, hasSystemAccess: true,
            new DateOnly(1990, 1, 1), "British", "Prefer not to say", number,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now));
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_Returns_NotConfigured_When_Company_Has_No_Settings_Row()
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context, CompanyWorkEmailSettings.Default)
            .HandleAsync(Request(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkEmailSuggestionStatus.NotConfigured, result.Value!.Status);
        Assert.Null(result.Value.Suggestion);
        Assert.Empty(result.Value.Domains);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotConfigured_Rather_Than_Disabled_When_Disabled_Without_A_Primary_Domain()
    {
        await using var context = BuildContext();
        var settings = new CompanyWorkEmailSettings(false, null, [], WorkEmailNamingConvention.FirstNameDotLastName);

        var result = await BuildHandler(context, settings).HandleAsync(Request(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(WorkEmailSuggestionStatus.NotConfigured, result.Value!.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_Disabled_When_Disabled_Even_If_Domain_Is_Configured()
    {
        await using var context = BuildContext();
        var settings = new CompanyWorkEmailSettings(false, "example.com", [], WorkEmailNamingConvention.FirstNameDotLastName);

        var result = await BuildHandler(context, settings).HandleAsync(Request(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(WorkEmailSuggestionStatus.Disabled, result.Value!.Status);
        Assert.Null(result.Value.Suggestion);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotConfigured_When_Enabled_Without_A_Primary_Domain()
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context, Enabled(primary: null))
            .HandleAsync(Request(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(WorkEmailSuggestionStatus.NotConfigured, result.Value!.Status);
        Assert.Null(result.Value.Suggestion);
        Assert.Null(result.Value.SelectedDomain);
        Assert.Empty(result.Value.Domains);
    }

    [Theory]
    [InlineData(null, "Smith")]
    [InlineData("", "Smith")]
    [InlineData("   ", "Smith")]
    [InlineData("Jane", null)]
    [InlineData("Jane", "")]
    [InlineData("Jane", "   ")]
    [InlineData("Jane", "'-")]
    public async Task HandleAsync_Returns_NameIncomplete_When_First_Or_Last_Name_Is_Missing(string? first, string? last)
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context, Enabled())
            .HandleAsync(Request(Guid.NewGuid(), first, last), CancellationToken.None);

        Assert.Equal(WorkEmailSuggestionStatus.NameIncomplete, result.Value!.Status);
        Assert.Null(result.Value.Suggestion);
        Assert.Equal("example.com", result.Value.SelectedDomain);
        Assert.Equal(["example.com"], result.Value.Domains);
    }

    [Fact]
    public async Task HandleAsync_Returns_Available_Suggestion_When_No_Employee_Has_The_Address()
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context, Enabled())
            .HandleAsync(Request(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(WorkEmailSuggestionStatus.Available, result.Value!.Status);
        Assert.Equal("jane.smith@example.com", result.Value.Suggestion);
        Assert.Equal("example.com", result.Value.SelectedDomain);
    }

    [Theory]
    [InlineData(WorkEmailNamingConvention.FirstNameDotLastName, "jane.smith@example.com")]
    [InlineData(WorkEmailNamingConvention.FirstInitialDotLastName, "j.smith@example.com")]
    [InlineData(WorkEmailNamingConvention.FirstNameLastName, "janesmith@example.com")]
    [InlineData(WorkEmailNamingConvention.FirstName, "jane@example.com")]
    public async Task HandleAsync_Uses_Configured_Naming_Convention(WorkEmailNamingConvention convention, string expected)
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context, Enabled(convention: convention))
            .HandleAsync(Request(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(expected, result.Value!.Suggestion);
    }

    [Fact]
    public async Task HandleAsync_Normalises_Names_With_Apostrophes_Hyphens_And_Accents()
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context, Enabled())
            .HandleAsync(Request(Guid.NewGuid(), "Anne-Marie", "O'Brién"), CancellationToken.None);

        Assert.Equal("annemarie.obrien@example.com", result.Value!.Suggestion);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unavailable_Without_Numbered_Alternative_When_Address_Is_Taken()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        await AddEmployeeAsync(context, companyId, "jane.smith@example.com");

        var result = await BuildHandler(context, Enabled()).HandleAsync(Request(companyId), CancellationToken.None);

        Assert.Equal(WorkEmailSuggestionStatus.Unavailable, result.Value!.Status);
        Assert.Equal("jane.smith@example.com", result.Value.Suggestion);
        Assert.Matches(@"^[a-z.]+@example\.com$", result.Value.Suggestion!);
    }

    [Fact]
    public async Task HandleAsync_Duplicate_Check_Is_Case_Insensitive_For_Entered_Names()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        await AddEmployeeAsync(context, companyId, "jane.smith@example.com");

        var result = await BuildHandler(context, Enabled())
            .HandleAsync(Request(companyId, "  JANE ", "sMiTh"), CancellationToken.None);

        Assert.Equal(WorkEmailSuggestionStatus.Unavailable, result.Value!.Status);
    }

    [Fact]
    public async Task HandleAsync_Duplicate_Check_Is_Scoped_To_The_Company()
    {
        await using var context = BuildContext();
        var otherCompanyId = Guid.NewGuid();
        await AddEmployeeAsync(context, otherCompanyId, "jane.smith@example.com");

        var result = await BuildHandler(context, Enabled()).HandleAsync(Request(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(WorkEmailSuggestionStatus.Available, result.Value!.Status);
        Assert.Equal("jane.smith@example.com", result.Value.Suggestion);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Treat_A_Different_Address_As_A_Duplicate()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        await AddEmployeeAsync(context, companyId, "jane.smithe@example.com");

        var result = await BuildHandler(context, Enabled()).HandleAsync(Request(companyId), CancellationToken.None);

        Assert.Equal(WorkEmailSuggestionStatus.Available, result.Value!.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_All_Domains_Primary_First_And_Defaults_To_Primary()
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context, Enabled(additional: ["alt.example.com", "other.org"]))
            .HandleAsync(Request(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(["example.com", "alt.example.com", "other.org"], result.Value!.Domains);
        Assert.Equal("example.com", result.Value.SelectedDomain);
        Assert.Equal("jane.smith@example.com", result.Value.Suggestion);
    }

    [Theory]
    [InlineData("alt.example.com")]
    [InlineData("@ALT.Example.com")]
    [InlineData("  alt.example.com  ")]
    public async Task HandleAsync_Uses_Selected_Additional_Domain(string requested)
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context, Enabled(additional: ["alt.example.com"]))
            .HandleAsync(Request(Guid.NewGuid(), domain: requested), CancellationToken.None);

        Assert.Equal("alt.example.com", result.Value!.SelectedDomain);
        Assert.Equal("jane.smith@alt.example.com", result.Value.Suggestion);
    }

    [Theory]
    [InlineData("unknown.com")]
    [InlineData("not a domain")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_Falls_Back_To_Primary_When_Domain_Is_Not_Configured(string requested)
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context, Enabled(additional: ["alt.example.com"]))
            .HandleAsync(Request(Guid.NewGuid(), domain: requested), CancellationToken.None);

        Assert.Equal("example.com", result.Value!.SelectedDomain);
        Assert.Equal("jane.smith@example.com", result.Value.Suggestion);
    }

    [Fact]
    public async Task HandleAsync_Availability_Depends_On_The_Selected_Domain()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        await AddEmployeeAsync(context, companyId, "jane.smith@example.com");
        var handler = BuildHandler(context, Enabled(additional: ["alt.example.com"]));

        var primary = await handler.HandleAsync(Request(companyId), CancellationToken.None);
        var alternative = await handler.HandleAsync(Request(companyId, domain: "alt.example.com"), CancellationToken.None);

        Assert.Equal(WorkEmailSuggestionStatus.Unavailable, primary.Value!.Status);
        Assert.Equal(WorkEmailSuggestionStatus.Available, alternative.Value!.Status);
    }

    [Fact]
    public async Task HandleAsync_Reads_Settings_For_The_Requested_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var reader = new FakeCompanyWorkEmailSettingsReader(Enabled());

        await new SuggestWorkEmailHandler(context, reader).HandleAsync(Request(companyId), CancellationToken.None);

        Assert.Equal(companyId, reader.LastCompanyId);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
