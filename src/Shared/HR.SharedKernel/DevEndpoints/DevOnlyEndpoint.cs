using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HR.SharedKernel.DevEndpoints;

/// <summary>
/// Which extra switch (on top of the always-required Development environment) a dev/test-only
/// endpoint needs before it is reachable.
/// </summary>
public enum DevEndpointKind
{
    /// <summary>Developer tooling: Development environment AND <c>DevTools:Enabled=true</c>.</summary>
    DevTools,

    /// <summary>Playwright E2E support: Development environment AND <c>E2E_TESTING=true</c>.</summary>
    E2E,
}

/// <summary>
/// Marks a FastEndpoints endpoint as dev/E2E-only. Every route under <c>/api/dev</c> MUST carry this
/// attribute (API startup refuses to boot otherwise). The attribute drives two fail-closed layers:
///  1. Discovery: outside Development the endpoint is never registered (route absent, so 404).
///  2. Per-request gate (<see cref="DevOnlyEndpointPreProcessor"/>): even when registered, the request
///     gets 404 unless the environment is Development AND the <see cref="DevEndpointKind"/> switch is on.
/// The gate runs before binding/handler, so no dev logic executes when it is closed. Endpoints that
/// mutate data must still perform their own authentication/policy/tenant checks.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DevOnlyEndpointAttribute(DevEndpointKind kind) : Attribute
{
    public DevEndpointKind Kind { get; } = kind;
}

/// <summary>Single source of truth for whether dev/E2E-only functionality is currently available.</summary>
public static class DevEndpointGate
{
    public const string DevRoutePrefix = "/api/dev";
    public const string E2ETestingVariable = "E2E_TESTING";

    /// <summary>
    /// True only in the Development environment. E2E_TESTING and DevTools:Enabled never widen this;
    /// they can only further narrow availability.
    /// </summary>
    public static bool IsEnvironmentPermitted(IHostEnvironment environment) => environment.IsDevelopment();

    public static bool IsE2ETestingEnabled() => string.Equals(
        Environment.GetEnvironmentVariable(E2ETestingVariable), "true", StringComparison.OrdinalIgnoreCase);

    public static bool IsAvailable(IServiceProvider services, DevEndpointKind kind)
    {
        var environment = services.GetService<IHostEnvironment>();
        if (environment is null || !IsEnvironmentPermitted(environment))
            return false;

        return kind switch
        {
            DevEndpointKind.E2E => IsE2ETestingEnabled(),
            DevEndpointKind.DevTools => services.GetService<IConfiguration>()?.GetValue<bool>("DevTools:Enabled") ?? false,
            _ => false,
        };
    }

    /// <summary>
    /// Type-discovery filter for <c>AddFastEndpoints(o =&gt; o.Filter = ...)</c>: dev-only endpoint types
    /// are only discovered (so only routed) in Development. Non-attributed endpoints under
    /// <c>/api/dev</c> are rejected at startup by <see cref="Configure"/>.
    /// </summary>
    public static bool ShouldDiscover(Type endpointType, IHostEnvironment environment) =>
        !Attribute.IsDefined(endpointType, typeof(DevOnlyEndpointAttribute), inherit: false)
        || IsEnvironmentPermitted(environment);

    /// <summary>
    /// Endpoint configurator: attaches the per-request gate to every attributed endpoint and throws
    /// (fail closed, at startup) if a route under <c>/api/dev</c> is missing the attribute.
    /// </summary>
    public static void Configure(EndpointDefinition endpoint)
    {
        var attribute = GetAttribute(endpoint);
        if (attribute is null)
        {
            if (endpoint.Routes.Any(r => r.StartsWith(DevRoutePrefix, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Endpoint {endpoint.EndpointType.FullName} is routed under '{DevRoutePrefix}' but is not marked "
                    + "[DevOnlyEndpoint]. Dev/E2E endpoints must use the shared gate.");
            }

            return;
        }

        endpoint.PreProcessors(Order.Before, new DevOnlyEndpointPreProcessor(attribute.Kind));
    }

    private static DevOnlyEndpointAttribute? GetAttribute(EndpointDefinition endpoint) =>
        (DevOnlyEndpointAttribute?)Attribute.GetCustomAttribute(endpoint.EndpointType, typeof(DevOnlyEndpointAttribute), inherit: false);
}

/// <summary>Fail-closed per-request gate: answers 404 unless <see cref="DevEndpointGate.IsAvailable"/>.</summary>
public sealed class DevOnlyEndpointPreProcessor(DevEndpointKind kind) : IGlobalPreProcessor
{
    public async Task PreProcessAsync(IPreProcessorContext context, CancellationToken cancellationToken)
    {
        if (DevEndpointGate.IsAvailable(context.HttpContext.RequestServices, kind))
            return;

        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status404NotFound;
        await response.StartAsync(cancellationToken);
    }
}
