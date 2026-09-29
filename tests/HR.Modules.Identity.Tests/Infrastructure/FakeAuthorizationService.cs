using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Identity.Tests.Infrastructure;

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
