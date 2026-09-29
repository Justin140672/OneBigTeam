using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FastEndpoints;

namespace HR.Architecture.Tests;

public class TenantScopedRouteConventionTests
{
    internal static readonly Assembly[] ModuleAssemblies =
        [
            typeof(HR.Modules.Companies.CompaniesModule).Assembly,
            typeof(HR.Modules.CompanyOnboarding.CompanyOnboardingModule).Assembly,
            typeof(HR.Modules.DataImport.DataImportModule).Assembly,
            typeof(HR.Modules.Identity.IdentityModule).Assembly,
            typeof(HR.Modules.Employees.EmployeesModule).Assembly,
            typeof(HR.Modules.Leave.LeaveModule).Assembly,
            typeof(HR.Modules.Documents.DocumentsModule).Assembly,
            typeof(HR.Modules.Tasks.TasksModule).Assembly,
            typeof(HR.Modules.Notifications.NotificationsModule).Assembly,
            typeof(HR.Modules.Probation.ProbationModule).Assembly,
            typeof(HR.Modules.Reporting.ReportingModule).Assembly,
            typeof(HR.Modules.Recruitment.RecruitmentModule).Assembly,
            typeof(HR.Modules.Assets.AssetsModule).Assembly,
            typeof(HR.Modules.Sickness.SicknessModule).Assembly,
            typeof(HR.Modules.Onboarding.OnboardingModule).Assembly,
            typeof(HR.Modules.Offboarding.OffboardingModule).Assembly,
            typeof(HR.Modules.Support.SupportModule).Assembly,
            typeof(HR.Modules.Marketing.MarketingModule).Assembly,
        ];

    /// <summary>
    /// Documented, deliberate exceptions to the "<c>/api/companies/{companyId}/...</c> first
    /// parameter must be named <c>companyId</c>" rule. Every entry here is a route where the first
    /// <c>/api/companies/</c> path parameter intentionally does NOT identify the caller's own
    /// tenant and therefore must NOT be named <c>companyId</c> (which would make
    /// TenantRouteAuthorizationMiddleware 403 legitimate traffic).
    ///
    /// Currently empty: there are no such routes. Add a route template string here (exact match,
    /// as written in the endpoint's <c>Get/Post/Put/Delete/Patch</c> call) together with a comment
    /// explaining why the exception is safe, e.g. a platform-admin route where the segment
    /// identifies the customer being administered rather than the caller.
    /// </summary>
    private static readonly string[] AllowedExceptionRouteTemplates = [];

    /// <summary>
    /// Ratchet: the number of routes under <c>/api/companies/{...}</c>. If this drops, endpoints
    /// have silently fallen out of inspection (or been deleted - lower the floor deliberately in
    /// that case). It may grow freely.
    /// </summary>
    private const int MinimumExpectedCompanyScopedRoutes = 400;

    private static readonly Regex FirstCompaniesParam =
        new(@"^/api/companies/\{(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    [Fact]
    public void Company_Scoped_Routes_Name_Their_First_Path_Parameter_companyId()
    {
        var violations = new List<string>();
        var inspected = 0;
        var (routesByEndpoint, failures) = InspectAll();

        Assert.True(failures.Count == 0,
            "Endpoint route inspection failed for the following endpoints (they would otherwise " +
            "silently escape the tenant-route check):" + Environment.NewLine +
            string.Join(Environment.NewLine, failures));

        foreach (var (endpointType, routes) in routesByEndpoint)
        {
            foreach (var route in routes)
            {
                if (!route.StartsWith("/api/companies/{", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                inspected++;

                if (AllowedExceptionRouteTemplates.Contains(route, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var match = FirstCompaniesParam.Match(route);
                var paramName = match.Success ? match.Groups["name"].Value : "(unparseable)";

                if (!string.Equals(paramName, "companyId", StringComparison.Ordinal))
                {
                    violations.Add($"{endpointType.FullName}: route '{route}' uses first path " +
                                   $"parameter '{{{paramName}}}' — must be '{{companyId}}' so " +
                                   "TenantRouteAuthorizationMiddleware enforces tenant isolation.");
                }
            }
        }

        Assert.True(inspected > 0,
            "Expected to inspect at least one '/api/companies/{...}' route — the route-reading " +
            "reflection helper is probably broken.");

        Assert.True(violations.Count == 0,
            "Tenant-scoped route naming violations (SEC-001 regression risk):" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Every_Endpoint_Is_Inspected_And_Declares_A_Route()
    {
        var (routesByEndpoint, failures) = InspectAll();
        var allTypes = AllEndpointTypes().ToList();

        Assert.True(failures.Count == 0,
            "Endpoint inspection failures:" + Environment.NewLine + string.Join(Environment.NewLine, failures));

        var notInspected = allTypes.Where(t => !routesByEndpoint.ContainsKey(t)).Select(t => t.FullName).ToList();
        Assert.True(notInspected.Count == 0,
            "Endpoints not inspected: " + string.Join(", ", notInspected));

        var noRoutes = routesByEndpoint.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key.FullName).ToList();
        Assert.True(noRoutes.Count == 0,
            "Endpoints that declare no route (route reading is broken or the endpoint is unreachable): "
            + string.Join(", ", noRoutes));

        var companyScoped = routesByEndpoint.Values
            .SelectMany(r => r)
            .Count(r => r.StartsWith("/api/companies/{", StringComparison.OrdinalIgnoreCase));

        Assert.True(companyScoped >= MinimumExpectedCompanyScopedRoutes,
            $"Only {companyScoped} company-scoped routes inspected; expected at least " +
            $"{MinimumExpectedCompanyScopedRoutes}. Endpoints have dropped out of inspection.");
    }

    [Fact]
    public void Route_Inspection_Failures_Are_Reported_With_Type_And_Message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ReadRouteTemplates(typeof(BrokenEndpointForTest)));
        Assert.Contains("boom", ex.Message);
        Assert.Contains(nameof(InvalidOperationException), ex.Message);
    }

    private sealed class BrokenEndpointForTest : EndpointWithoutRequest
    {
        public override void Configure() => throw new InvalidOperationException("boom");
    }

    /// <summary>
    /// Reads the route templates an endpoint declares in <c>Configure()</c>. Any failure is
    /// surfaced (never swallowed) so an endpoint can never silently drop out of the inspection.
    /// </summary>
    internal static IReadOnlyCollection<string> ReadRouteTemplates(Type endpointType)
    {
        var endpoint = (BaseEndpoint)RuntimeHelpers.GetUninitializedObject(endpointType);

        var definition = (EndpointDefinition)RuntimeHelpers
            .GetUninitializedObject(typeof(EndpointDefinition));

        typeof(BaseEndpoint)
            .GetProperty("Definition", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(endpoint, definition);

        var configure = endpointType.GetMethod("Configure", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{endpointType.FullName}: no public Configure() method found.");

        try
        {
            configure.Invoke(endpoint, null);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw new InvalidOperationException(
                $"{endpointType.FullName}: Configure() threw {tie.InnerException.GetType().FullName}: " +
                tie.InnerException.Message, tie.InnerException);
        }

        var routesProp = typeof(EndpointDefinition)
            .GetProperty("Routes", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("EndpointDefinition.Routes property not found.");

        return routesProp.GetValue(definition) as string[] ?? [];
    }

    internal static IEnumerable<Type> AllEndpointTypes() =>
        ModuleAssemblies.SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(IEndpoint).IsAssignableFrom(t)
                        && t.GetCustomAttribute<CompilerGeneratedAttribute>() is null
                        && !t.Name.Contains("ForTest"));

    /// <summary>
    /// Inspects every endpoint. Failures are collected with endpoint type, exception type and
    /// message, and returned so callers can fail the test.
    /// </summary>
    internal static (Dictionary<Type, IReadOnlyCollection<string>> Routes, List<string> Failures) InspectAll()
    {
        var routes = new Dictionary<Type, IReadOnlyCollection<string>>();
        var failures = new List<string>();

        foreach (var endpointType in AllEndpointTypes())
        {
            try
            {
                routes[endpointType] = ReadRouteTemplates(endpointType);
            }
            catch (Exception ex)
            {
                failures.Add($"{endpointType.FullName}: {ex.GetType().FullName}: {ex.Message}");
            }
        }

        return (routes, failures);
    }
}
