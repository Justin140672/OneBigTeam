using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="IAuthorizationService"/> used to exercise the OBT-721 workload
/// action providers' self-enforced policy checks (e.g. "reporting:view-hr") without booting real
/// ASP.NET Core authorization/policy infrastructure. Construct with the set of policy names that
/// should succeed for the caller under test; every other policy name fails.
/// </summary>
internal sealed class FakeAuthorizationService(params string[] succeededPolicies) : IAuthorizationService
{
    private readonly HashSet<string> _succeededPolicies = new(succeededPolicies, StringComparer.Ordinal);

    public Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
        Task.FromResult(AuthorizationResult.Failed());

    public Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user, object? resource, string policyName) =>
        Task.FromResult(_succeededPolicies.Contains(policyName)
            ? AuthorizationResult.Success()
            : AuthorizationResult.Failed());
}
