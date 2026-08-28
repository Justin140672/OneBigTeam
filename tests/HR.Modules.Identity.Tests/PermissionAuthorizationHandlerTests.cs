using System.Security.Claims;
using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Identity.Tests;

using AppAuthorizationService = HR.SharedKernel.IAuthorizationService;

/// <summary>
/// IAM-06: unit tests for <see cref="PermissionAuthorizationHandler"/>, the mechanism every named
/// capability policy now runs through instead of a hard-coded inline role list.
/// </summary>
public class PermissionAuthorizationHandlerTests
{
    private static readonly Guid PermissionId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static PermissionDenialAuditThrottle NewThrottle() =>
        new(new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

    /// <summary>Minimal fake for HR.SharedKernel.IAuthorizationService, configurable per test.</summary>
    private sealed class FakeAppAuthorizationService(bool hasPermission) : AppAuthorizationService
    {
        public Task<bool> HasPermissionAsync(Guid userId, Guid permissionId, CancellationToken ct = default) =>
            Task.FromResult(hasPermission);

        public Task<IReadOnlySet<Guid>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

        public Task<IReadOnlySet<Guid>> GetEffectiveRolesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
    }

    private static async Task<AuthorizationHandlerContext> RunAsync(
        PermissionAuthorizationHandler handler, PermissionRequirement requirement)
    {
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), resource: null);
        await handler.HandleAsync(context);
        return context;
    }

    [Fact]
    public async Task Succeeds_When_Current_User_Has_The_Required_Permission()
    {
        var handler = new PermissionAuthorizationHandler(
            FakeCurrentUser.Authenticated(UserId, CompanyId.ToString()),
            new FakeAppAuthorizationService(hasPermission: true),
            NewThrottle(),
            new FakeAuditEventPublisher(),
            new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var context = await RunAsync(handler, new PermissionRequirement(PermissionId));

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Does_Not_Succeed_When_Current_User_Lacks_The_Required_Permission()
    {
        var handler = new PermissionAuthorizationHandler(
            FakeCurrentUser.Authenticated(UserId, CompanyId.ToString()),
            new FakeAppAuthorizationService(hasPermission: false),
            NewThrottle(),
            new FakeAuditEventPublisher(),
            new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var context = await RunAsync(handler, new PermissionRequirement(PermissionId));

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Does_Not_Succeed_When_Current_User_Id_Is_Null()
    {
        // Even a "would-be-granted" authorization service must not be consulted/succeed for an
        // unauthenticated/anonymous caller.
        var handler = new PermissionAuthorizationHandler(
            FakeCurrentUser.Anonymous,
            new FakeAppAuthorizationService(hasPermission: true),
            NewThrottle(),
            new FakeAuditEventPublisher(),
            new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var context = await RunAsync(handler, new PermissionRequirement(PermissionId));

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Publishes_A_Denial_Audit_Event_When_Permission_Is_Denied()
    {
        var publisher = new FakeAuditEventPublisher();
        var handler = new PermissionAuthorizationHandler(
            FakeCurrentUser.Authenticated(UserId, CompanyId.ToString()),
            new FakeAppAuthorizationService(hasPermission: false),
            NewThrottle(),
            publisher,
            new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        await RunAsync(handler, new PermissionRequirement(PermissionId));

        var published = Assert.Single(publisher.PublishedEvents);
        var deniedEvent = Assert.IsType<PermissionDeniedAuditEvent>(published);
        Assert.Equal(CompanyId, deniedEvent.CompanyId);
        Assert.Equal(UserId, deniedEvent.UserId);
        Assert.Equal(PermissionId, deniedEvent.PermissionId);
        Assert.False(deniedEvent.IsRepeatedEscalation);
    }

    [Fact]
    public async Task Does_Not_Publish_A_Denial_Audit_Event_When_Tenant_Cannot_Be_Resolved()
    {
        var publisher = new FakeAuditEventPublisher();
        // No tenantId supplied — same as an authenticated caller whose tenant could not be resolved.
        var handler = new PermissionAuthorizationHandler(
            FakeCurrentUser.Authenticated(UserId),
            new FakeAppAuthorizationService(hasPermission: false),
            NewThrottle(),
            publisher,
            new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        await RunAsync(handler, new PermissionRequirement(PermissionId));

        Assert.Empty(publisher.PublishedEvents);
    }
}
