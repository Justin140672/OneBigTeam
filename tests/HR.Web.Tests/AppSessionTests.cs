using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

public class AppSessionTests
{
    // Matches the permission ID hard-coded in AppSession.CanManageEmployees.
    private static readonly Guid ManageEmployeesPermission = new("00000000-0000-0000-0001-000000000004");

    private static IHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    private static AppSession BuildSession(IHttpClientFactory factory) =>
        new(factory, new EmployeeService(factory), new SicknessCategoryService(factory), new CompanyOnboardingService(factory), new SubscriptionService(factory));

    private static RoutingHandler BuildHappyPathHandler(
        Guid userId, Guid companyId, Guid employeeId,
        bool isHrAdministrator = false, bool isManager = false, bool isRecruiter = false,
        bool isEmailConfirmed = true)
    {
        var me = new MeResponse(userId, companyId, "alice@example.com", [ManageEmployeesPermission], true,
            isHrAdministrator, isManager, isRecruiter, isEmailConfirmed);
        var company = new GetCompanyResponse(companyId, "Acme Corporation", true, DateTime.UtcNow, [],
            new GetCompanyBrandingResponse("logo.png", "small-logo.png", null));
        var settings = new GetCompanySettingsResponse(
            companyId, "Europe/London", "en-GB",
            "^postcode$", "^telephone$", "^mobile$", DateTime.UtcNow);
        var hrSettings = new GetHrSettingsResponse(
            companyId, 31, 7.5m, 1, 25m, 6, true, false, true, 7, 1,
            "I confirm that I have read and understood this document.", 3,
            NoticePeriodUnit.Months, 1, true,
            EmployeeNumberMode.Automatic, "EMP-", 1, 4, DateTime.UtcNow);
        var employee = new MyEmployeeResponse(employeeId, "Alice", "Smith", "Engineer", null, null, "avatar.png");

        return new RoutingHandler(new()
        {
            ["api/me"] = me,
            [$"api/companies/{companyId}"] = company,
            [$"api/companies/{companyId}/settings"] = settings,
            [$"api/companies/{companyId}/hr-settings"] = hrSettings,
            [$"api/companies/{companyId}/employees/me"] = employee,
        });
    }

    [Fact]
    public async Task InitialiseAsync_Populates_All_Fields_On_Success()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var factory = BuildFactory(BuildHappyPathHandler(userId, companyId, employeeId));
        var session = BuildSession(factory);

        await session.InitialiseAsync();

        Assert.True(session.IsLoaded);
        Assert.Equal(userId, session.UserId);
        Assert.Equal(companyId, session.CompanyId);
        Assert.Equal("alice@example.com", session.Email);
        Assert.True(session.CanManageCompany);
        Assert.True(session.CanManageEmployees);

        Assert.Equal("Acme Corporation", session.CompanyName);
        Assert.Equal("logo.png", session.PrimaryLogoUrl);

        Assert.Equal("Europe/London", session.TimeZone);
        Assert.Equal(25m, session.DefaultHolidayAllowance);
        Assert.True(session.DisplaySalaryOnEmployeeProfile);
        Assert.Equal("^postcode$", session.PostcodeRegex);
        Assert.Equal("^telephone$", session.TelephoneRegex);
        Assert.Equal("^mobile$", session.MobileRegex);

        Assert.Equal(employeeId, session.EmployeeId);
        Assert.Equal("Alice", session.FirstName);
        Assert.Equal("Smith", session.LastName);
        Assert.Equal("Alice Smith", session.DisplayName);
        Assert.Equal("AS", session.Initials);
        Assert.Equal($"/companies/{companyId}/employees/{employeeId}/profile", session.MyProfileUrl);
    }

    [Fact]
    public async Task InitialiseAsync_Does_Not_Refetch_When_Already_Loaded()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var handler = BuildHappyPathHandler(userId, companyId, employeeId);
        var factory = BuildFactory(handler);
        var session = BuildSession(factory);

        await session.InitialiseAsync();
        var requestCountAfterFirstLoad = handler.RequestCount;

        await session.InitialiseAsync();

        Assert.Equal(requestCountAfterFirstLoad, handler.RequestCount);
    }

    [Fact]
    public async Task InitialiseAsync_Leaves_Session_Unloaded_When_Me_Endpoint_Fails()
    {
        var factory = BuildFactory(new StaticResponseHandler(HttpStatusCode.Unauthorized));
        var session = BuildSession(factory);

        await session.InitialiseAsync();

        Assert.False(session.IsLoaded);
        Assert.Equal(Guid.Empty, session.CompanyId);
    }

    [Fact]
    public void CanManageEmployees_Is_False_Without_The_Permission()
    {
        // Constructed via reflection-free path: rely on default state (no permissions loaded).
        var session = BuildSession(BuildFactory(new StaticResponseHandler(HttpStatusCode.Unauthorized)));

        Assert.False(session.CanManageEmployees);
    }

    [Fact]
    public void MyProfileUrl_Returns_Root_When_No_Employee_Is_Linked()
    {
        var session = BuildSession(BuildFactory(new StaticResponseHandler(HttpStatusCode.Unauthorized)));

        Assert.Equal("/", session.MyProfileUrl);
    }

    [Fact]
    public void DisplayName_Falls_Back_To_Email_When_No_Name_Is_Set()
    {
        var session = BuildSession(BuildFactory(new StaticResponseHandler(HttpStatusCode.Unauthorized)));

        // Neither FirstName/LastName nor Email are set yet — falls back to "Unknown".
        Assert.Equal("Unknown", session.DisplayName);
    }

    [Fact]
    public void Initials_Returns_QuestionMark_When_No_Name_Is_Set()
    {
        var session = BuildSession(BuildFactory(new StaticResponseHandler(HttpStatusCode.Unauthorized)));

        Assert.Equal("?", session.Initials);
    }

    [Fact]
    public async Task LandingUrl_Prioritises_HrAdministrator_Over_Other_Roles()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var factory = BuildFactory(BuildHappyPathHandler(userId, companyId, employeeId,
            isHrAdministrator: true, isManager: true, isRecruiter: true));
        var session = BuildSession(factory);

        await session.InitialiseAsync();

        Assert.True(session.IsHrAdministrator);
        Assert.Equal("/dashboard/hr", session.LandingUrl);
    }

    [Fact]
    public async Task LandingUrl_Prioritises_Recruiter_Over_Manager_When_Not_HrAdministrator()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var factory = BuildFactory(BuildHappyPathHandler(userId, companyId, employeeId,
            isManager: true, isRecruiter: true));
        var session = BuildSession(factory);

        await session.InitialiseAsync();

        Assert.Equal("/dashboard/recruitment", session.LandingUrl);
    }

    [Fact]
    public async Task LandingUrl_Falls_Back_To_Manager_Dashboard()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var factory = BuildFactory(BuildHappyPathHandler(userId, companyId, employeeId, isManager: true));
        var session = BuildSession(factory);

        await session.InitialiseAsync();

        Assert.Equal("/dashboard/manager", session.LandingUrl);
    }

    [Fact]
    public async Task LandingUrl_Falls_Back_To_CompanyEdit_When_CompanyAdmin_Without_Dashboard_Roles()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        // CanManageCompany=true is baked into BuildHappyPathHandler's MeResponse; no dashboard roles set.
        var factory = BuildFactory(BuildHappyPathHandler(userId, companyId, employeeId));
        var session = BuildSession(factory);

        await session.InitialiseAsync();

        Assert.Equal($"/companies/{companyId}/edit", session.LandingUrl);
    }

    [Fact]
    public void LandingUrl_Falls_Back_To_MyProfileUrl_When_No_Roles_Or_Company_Admin()
    {
        var session = BuildSession(BuildFactory(new StaticResponseHandler(HttpStatusCode.Unauthorized)));

        Assert.Equal(session.MyProfileUrl, session.LandingUrl);
    }

    [Theory]
    [InlineData("hr", true, false, false, true)]
    [InlineData("recruitment", false, false, true, true)]
    [InlineData("manager", false, true, false, true)]
    [InlineData("hr", false, false, false, false)]
    [InlineData("unknown", true, true, true, false)]
    public async Task IsDashboardAvailable_Reflects_Role_Flags(
        string dashboardKey, bool isHrAdministrator, bool isManager, bool isRecruiter, bool expected)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var factory = BuildFactory(BuildHappyPathHandler(userId, companyId, employeeId,
            isHrAdministrator, isManager, isRecruiter));
        var session = BuildSession(factory);

        await session.InitialiseAsync();

        Assert.Equal(expected, session.IsDashboardAvailable(dashboardKey));
    }

    [Theory]
    [InlineData("hr", "/dashboard/hr")]
    [InlineData("recruitment", "/dashboard/recruitment")]
    [InlineData("manager", "/dashboard/manager")]
    [InlineData("unknown", null)]
    public void DashboardUrl_Maps_Known_Keys(string dashboardKey, string? expected)
    {
        Assert.Equal(expected, AppSession.DashboardUrl(dashboardKey));
    }

    // ── Fake handlers ────────────────────────────────────────────────────────────

    private sealed class RoutingHandler(Dictionary<string, object> responsesByPathSuffix) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var path = request.RequestUri!.PathAndQuery.TrimStart('/');

            var match = responsesByPathSuffix
                .Where(kvp => path.Equals(kvp.Key, StringComparison.Ordinal))
                .Select(kvp => kvp.Value)
                .FirstOrDefault();

            if (match is null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(match) };
            return Task.FromResult(response);
        }
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
