using System.Security.Claims;

using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;

using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Identity.Tests;

using AppAuthorizationService = HR.SharedKernel.IAuthorizationService;

public class RoleAuthorizationHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CompanyId = Guid.NewGuid();

    private sealed class FakeAppAuthorizationService(IReadOnlySet<Guid> effectiveRoles) : AppAuthorizationService
    {
        public Task<bool> HasPermissionAsync(Guid userId, Guid permissionId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<IReadOnlySet<Guid>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

        public Task<IReadOnlySet<Guid>> GetEffectiveRolesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(effectiveRoles);
    }

    private static async Task<AuthorizationHandlerContext> RunAsync(
        RoleAuthorizationHandler handler, RoleRequirement requirement)
    {
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), resource: null);
        await handler.HandleAsync(context);
        return context;
    }

    [Fact]
    public async Task SupportSession_Succeeds_For_Role_Employee_Requirement()
    {
        var handler = new RoleAuthorizationHandler(
            FakeCurrentUser.SupportSession(UserId, tenantId: CompanyId.ToString()),
            new FakeAppAuthorizationService(new HashSet<Guid>
            {
                SystemRoles.Employee, SystemRoles.Manager, SystemRoles.Recruiter,
                SystemRoles.HrAdministrator, SystemRoles.CompanyAdministrator,
            }));

        var context = await RunAsync(handler, new RoleRequirement(new HashSet<Guid> { SystemRoles.Employee }));

        Assert.True(context.HasSucceeded);
    }

    [Theory]
    [MemberData(nameof(NonEmployeeRoleIds))]
    public async Task SupportSession_Is_Denied_For_Every_Role_Other_Than_Employee(Guid deniedRoleId)
    {
        var handler = new RoleAuthorizationHandler(
            FakeCurrentUser.SupportSession(UserId, tenantId: CompanyId.ToString()),
            new FakeAppAuthorizationService(new HashSet<Guid>
            {
                SystemRoles.Employee, SystemRoles.Manager, SystemRoles.Recruiter,
                SystemRoles.HrAdministrator, SystemRoles.CompanyAdministrator,
            }));

        var context = await RunAsync(handler, new RoleRequirement(new HashSet<Guid> { deniedRoleId }));

        Assert.False(context.HasSucceeded);
    }

    public static IEnumerable<object[]> NonEmployeeRoleIds()
    {
        yield return [SystemRoles.Manager];
        yield return [SystemRoles.Recruiter];
        yield return [SystemRoles.HrAdministrator];
        yield return [SystemRoles.CompanyAdministrator];
    }

    [Fact]
    public async Task Non_SupportSession_User_Still_Resolves_Roles_Via_The_Authorization_Service()
    {
        var handler = new RoleAuthorizationHandler(
            FakeCurrentUser.Authenticated(UserId, CompanyId.ToString()),
            new FakeAppAuthorizationService(new HashSet<Guid> { SystemRoles.Manager }));

        var context = await RunAsync(handler, new RoleRequirement(new HashSet<Guid> { SystemRoles.Manager }));

        Assert.True(context.HasSucceeded);
    }
}
