using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FastEndpoints;

namespace HR.Architecture.Tests;

/// <summary>
/// SEC-001 / TEST-002: enforces the tenant-scoped route naming convention that
/// <see cref="HR.Modules.Identity.TenantRouteAuthorizationMiddleware"/> depends on.
///
/// That middleware only enforces cross-tenant isolation when a company-scoped route exposes the
/// company's id through a route parameter literally named <c>companyId</c>. If a route under
/// <c>/api/companies/{...}</c> names its first path parameter anything else (e.g.
/// <c>/api/companies/{id:guid}</c>), the middleware silently skips it and any authenticated user
/// can reach another tenant's data by supplying its GUID — exactly the SEC-001 regression.
///
/// This test reflects over every FastEndpoints <see cref="BaseEndpoint"/> subclass across the
/// module assemblies (same enumeration style as
/// <see cref="ResourceAuthorizationArchitectureTests"/>), reads each endpoint's configured route
/// templates by invoking <c>Configure()</c> against a stand-in <see cref="EndpointDefinition"/>,
/// and fails if any company-scoped route's first path parameter is not <c>companyId</c>.
/// </summary>
public class TenantScopedRouteConventionTests
{
    private static readonly Assembly[] ModuleAssemblies =
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

    private static readonly Regex FirstCompaniesParam =
        new(@"^/api/companies/\{(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    [Fact]
    public void Company_Scoped_Routes_Name_Their_First_Path_Parameter_companyId()
    {
        var violations = new List<string>();
        var inspected = 0;

        foreach (var assembly in ModuleAssemblies)
        {
            foreach (var endpointType in assembly.GetTypes()
                         .Where(t => t is { IsAbstract: false, IsClass: true }
                                     && typeof(IEndpoint).IsAssignableFrom(t)))
            {
                foreach (var route in TryReadRouteTemplates(endpointType))
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
        }

        Assert.True(inspected > 0,
            "Expected to inspect at least one '/api/companies/{...}' route — the route-reading " +
            "reflection helper is probably broken.");

        Assert.True(violations.Count == 0,
            "Tenant-scoped route naming violations (SEC-001 regression risk):" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Instantiates the endpoint without running its constructor, gives it a stand-in
    /// <see cref="EndpointDefinition"/>, runs <c>Configure()</c> (which is where FastEndpoints
    /// endpoints declare their verbs/routes) and returns the configured route templates.
    /// Any endpoint whose <c>Configure()</c> cannot be evaluated this way is skipped.
    /// </summary>
    private static IReadOnlyCollection<string> TryReadRouteTemplates(Type endpointType)
    {
        try
        {
            var endpoint = (BaseEndpoint)RuntimeHelpers.GetUninitializedObject(endpointType);

            var definition = (EndpointDefinition)RuntimeHelpers
                .GetUninitializedObject(typeof(EndpointDefinition));

            typeof(BaseEndpoint)
                .GetProperty("Definition", BindingFlags.Public | BindingFlags.Instance)!
                .SetValue(endpoint, definition);

            endpointType.GetMethod("Configure", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(endpoint, null);

            var routesProp = typeof(EndpointDefinition)
                .GetProperty("Routes", BindingFlags.Public | BindingFlags.Instance);

            return routesProp?.GetValue(definition) as string[] ?? [];
        }
        catch
        {
            return [];
        }
    }
}
