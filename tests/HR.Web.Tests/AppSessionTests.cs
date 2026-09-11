using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

public class AppSessionTests
{
    // Matches the permission ID hard-coded in AppSession.CanManageEmployees.
    private static readonly Guid ManageEmployeesPermission = new("00000000-0000-0000-0001-000000000004");

    // Matches the permission IDs hard-coded in AppSession.CanViewOnboarding / CanManageSupport
    // (OBT-IAM-09).
    private static readonly Guid OnboardingViewPermission = new("00000000-0000-0000-0001-000000000019");
    private static readonly Guid SupportManagePermission = new("00000000-0000-0000-0001-000000000042");

    private static HrApiHttpClientFactory BuildFactory(HttpMessageHandler handler, CircuitSessionState? sessionState = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return new HrApiHttpClientFactory(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), sessionState ?? new CircuitSessionState());
    }

    private static AppSession BuildSession(HrApiHttpClientFactory factory, CircuitSessionState? sessionState = null) =>
        new(factory, new EmployeeService(factory), new SicknessCategoryService(factory), new CompanyOnboardingService(factory), new SubscriptionService(factory), sessionState ?? new CircuitSessionState());

    private static RoutingHandler BuildHappyPathHandler(
        Guid userId, Guid companyId, Guid employeeId,
        bool isHrAdministrator = false, bool isManager = false, bool isRecruiter = false,
        bool isEmailConfirmed = true)
    {
        var me = new MeResponse(userId, companyId, "alice@example.com", [ManageEmployeesPermission], [], true,
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
            EmployeeNumberMode.Automatic, "EMP-", 1, 4,
            AssetNumberMode.Manual, null, 1, 1, DateTime.UtcNow, 9);
        var employee = new MyEmployeeResponse(employeeId, "Alice", "Smith", "Engineer", null, null, "avatar.png", false);

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

    // ── OBT-IAM-09: CanViewOnboarding / CanManageSupport permission-derived flags ──────────────

    private static RoutingHandler BuildHandlerWithPermissions(
        Guid userId, Guid companyId, Guid employeeId, IReadOnlyList<Guid> permissionIds,
        bool isHrAdministrator = false, GetCompanyOnboardingChecklistResponse? checklist = null)
    {
        var me = new MeResponse(userId, companyId, "alice@example.com", permissionIds.ToList(), [], true,
            isHrAdministrator, false, false, true);
        var company = new GetCompanyResponse(companyId, "Acme Corporation", true, DateTime.UtcNow, [],
            new GetCompanyBrandingResponse("logo.png", "small-logo.png", null));
        var settings = new GetCompanySettingsResponse(
            companyId, "Europe/London", "en-GB",
            "^postcode$", "^telephone$", "^mobile$", DateTime.UtcNow);
        var hrSettings = new GetHrSettingsResponse(
            companyId, 31, 7.5m, 1, 25m, 6, true, false, true, 7, 1,
            "I confirm that I have read and understood this document.", 3,
            NoticePeriodUnit.Months, 1, true,
            EmployeeNumberMode.Automatic, "EMP-", 1, 4,
            AssetNumberMode.Manual, null, 1, 1, DateTime.UtcNow, 9);
        var employee = new MyEmployeeResponse(employeeId, "Alice", "Smith", "Engineer", null, null, "avatar.png", false);

        var responses = new Dictionary<string, object>
        {
            ["api/me"] = me,
            [$"api/companies/{companyId}"] = company,
            [$"api/companies/{companyId}/settings"] = settings,
            [$"api/companies/{companyId}/hr-settings"] = hrSettings,
            [$"api/companies/{companyId}/employees/me"] = employee,
        };

        if (checklist is not null)
            responses["api/company-onboarding/checklist"] = checklist;

        return new RoutingHandler(responses);
    }

    [Fact]
    public async Task CanViewOnboarding_Is_True_Only_When_Permission_Present()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var factory = BuildFactory(BuildHandlerWithPermissions(
            userId, companyId, employeeId, [OnboardingViewPermission]));
        var session = BuildSession(factory);

        await session.InitialiseAsync();

        Assert.True(session.CanViewOnboarding);
        Assert.False(session.CanManageSupport);
    }

    [Fact]
    public async Task CanViewOnboarding_Is_False_Without_The_Permission()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var factory = BuildFactory(BuildHandlerWithPermissions(
            userId, companyId, employeeId, []));
        var session = BuildSession(factory);

        await session.InitialiseAsync();

        Assert.False(session.CanViewOnboarding);
    }

    [Fact]
    public async Task CanManageSupport_Is_True_Only_When_Permission_Present()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var factory = BuildFactory(BuildHandlerWithPermissions(
            userId, companyId, employeeId, [SupportManagePermission]));
        var session = BuildSession(factory);

        await session.InitialiseAsync();

        Assert.True(session.CanManageSupport);
        Assert.False(session.CanViewOnboarding);
    }

    [Fact]
    public async Task CanManageSupport_Is_False_Without_The_Permission()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var factory = BuildFactory(BuildHandlerWithPermissions(
            userId, companyId, employeeId, []));
        var session = BuildSession(factory);

        await session.InitialiseAsync();

        Assert.False(session.CanManageSupport);
    }

    // OBT-IAM-09: a Company-Administrator-only account (CanManageCompany=true) no longer holds
    // onboarding:view, so ShowGettingStarted's fetch must be skipped and LandingUrl must NOT route
    // to "/getting-started" for it — it falls through to the CanManageCompany branch instead. This
    // pins the regression the ticket fixed: previously LandingUrl's guard was
    // `IsHrAdministrator || CanManageCompany`, which incorrectly included this persona.
    [Fact]
    public async Task LandingUrl_Does_Not_Route_To_GettingStarted_For_CompanyAdministratorOnly()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        // CanManageCompany=true is baked into BuildHandlerWithPermissions' MeResponse
        // (hard-coded `true` for the CanManageCompany positional argument); no onboarding:view
        // permission granted, so the checklist fetch is skipped and ShowGettingStarted stays false
        // even though a checklist response is supplied here (it must never be requested).
        var checklist = new GetCompanyOnboardingChecklistResponse([], 0, IsHidden: false, IsDismissedEarly: false);
        var handler = BuildHandlerWithPermissions(
            userId, companyId, employeeId, [], isHrAdministrator: false, checklist: checklist);
        var factory = BuildFactory(handler);
        var session = BuildSession(factory);

        await session.InitialiseAsync();

        Assert.False(session.CanViewOnboarding);
        Assert.True(session.CanManageCompany);
        Assert.False(session.ShowGettingStarted);
        Assert.Equal($"/companies/{companyId}/edit", session.LandingUrl);
    }

    // OBT-IAM-09: once the account also holds onboarding:view (e.g. Company Administrator + HR
    // Administrator, or any future permission grant carrying it) the checklist is fetched and, if
    // not hidden/dismissed, LandingUrl routes to "/getting-started".
    [Fact]
    public async Task LandingUrl_Routes_To_GettingStarted_When_CanViewOnboarding_And_Checklist_Not_Hidden()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var checklist = new GetCompanyOnboardingChecklistResponse([], 0, IsHidden: false, IsDismissedEarly: false);
        var handler = BuildHandlerWithPermissions(
            userId, companyId, employeeId, [OnboardingViewPermission], isHrAdministrator: false, checklist: checklist);
        var factory = BuildFactory(handler);
        var session = BuildSession(factory);

        await session.InitialiseAsync();

        Assert.True(session.CanViewOnboarding);
        Assert.True(session.ShowGettingStarted);
        Assert.Equal("/getting-started", session.LandingUrl);
    }

    // ── Ticket 12: cached identity must not survive circuit invalidation ───────────────────────
    // InitialiseAsync now compares CircuitSessionState.Status/AccessToken against the token this
    // session's fields were actually loaded for (_loadedForToken) — see AppSession.InitialiseAsync's
    // own remarks. This is covered only indirectly elsewhere (by the AppSessionAuthStateProvider
    // tests proving Status transitions correctly) and by a Playwright E2E test; this is the one
    // direct AppSession-level test proving the cache itself is discarded and reloaded rather than
    // silently keeping a previous identity's data once the shared CircuitSessionState it was built
    // against has been invalidated by a later SetToken/Clear sequence.
    [Fact]
    public async Task InitialiseAsync_Reloads_When_SessionState_Was_Invalidated_After_The_Initial_Load()
    {
        var userIdA = Guid.NewGuid();
        var userIdB = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var sessionState = new CircuitSessionState();
        sessionState.SetToken("token-a");

        // A handler whose response identity flips after the first api/me call, standing in for "the
        // backend now represents a different signed-in identity" — the same shared AppSession
        // instance below is asked to InitialiseAsync twice against this one factory, so a genuine
        // reload after invalidation is the only way userIdB's data could ever be observed.
        var handler = new SwitchingIdentityHandler(
            BuildHappyPathHandler(userIdA, companyId, employeeId),
            BuildHappyPathHandler(userIdB, companyId, employeeId));
        var factory = BuildFactory(handler, sessionState);
        var session = BuildSession(factory, sessionState);

        await session.InitialiseAsync();
        Assert.True(session.IsLoaded);
        Assert.Equal(userIdA, session.UserId);

        var requestCountAfterFirstLoad = handler.RequestCount;

        // Same SAME AppSession instance: if InitialiseAsync still (incorrectly) trusted a stale
        // IsLoaded==true with no live-state check, this would be a pure no-op and userIdA's cached
        // fields would remain forever, even though the circuit has since been invalidated.
        await session.InitialiseAsync();
        Assert.Equal(requestCountAfterFirstLoad, handler.RequestCount);
        Assert.Equal(userIdA, session.UserId);

        // Simulate what AppSessionAuthStateProvider.ApplyState does on a rejected different-identity
        // reconnect: Clear() flips an Authenticated circuit to sticky Invalidated, and a brand new
        // token then arrives on what production code treats as a genuinely fresh circuit — but here
        // we drive the SAME AppSession instance to prove its own guard, independent of ApplyState.
        sessionState.Clear();
        Assert.Equal(CircuitAuthStatus.Invalidated, sessionState.Status);
        sessionState.SetToken("token-b");

        await session.InitialiseAsync();

        Assert.True(session.IsLoaded);
        Assert.True(handler.RequestCount > requestCountAfterFirstLoad,
            "InitialiseAsync must re-fetch once the shared CircuitSessionState has moved past the token this session's cache was loaded for.");
        Assert.Equal(userIdB, session.UserId);
    }

    // ── Fake handlers ────────────────────────────────────────────────────────────

    private sealed class RoutingHandler(Dictionary<string, object> responsesByPathSuffix) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        // Exposes the protected HttpMessageHandler.SendAsync so SwitchingIdentityHandler can delegate
        // to an inner RoutingHandler instance directly instead of duplicating its routing logic.
        public Task<HttpResponseMessage> PublicSendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            SendAsync(request, cancellationToken);

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

    // Routes every request to firstHandler until api/me has been requested once, then routes every
    // subsequent request to secondHandler — used by
    // InitialiseAsync_Reloads_When_SessionState_Was_Invalidated_After_The_Initial_Load to simulate
    // "the backend now represents a different identity" across two InitialiseAsync calls made
    // against the very same AppSession/HttpMessageHandler.
    private sealed class SwitchingIdentityHandler(RoutingHandler firstHandler, RoutingHandler secondHandler) : HttpMessageHandler
    {
        private int _meRequestCount;

        public int RequestCount => firstHandler.RequestCount + secondHandler.RequestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery.TrimStart('/');
            var useSecond = _meRequestCount > 0;
            if (path.Equals("api/me", StringComparison.Ordinal))
                _meRequestCount++;

            var inner = useSecond ? secondHandler : firstHandler;
            return InvokeSendAsync(inner, request, cancellationToken);
        }

        private static Task<HttpResponseMessage> InvokeSendAsync(
            RoutingHandler inner, HttpRequestMessage request, CancellationToken cancellationToken) =>
            inner.PublicSendAsync(request, cancellationToken);
    }
}
