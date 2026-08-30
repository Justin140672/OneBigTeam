using System.Security.Claims;
using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;

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
            new CapturingAdministrativeAlertWriter(),
            NullLogger<PermissionAuthorizationHandler>.Instance,
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
            new CapturingAdministrativeAlertWriter(),
            NullLogger<PermissionAuthorizationHandler>.Instance,
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
            new CapturingAdministrativeAlertWriter(),
            NullLogger<PermissionAuthorizationHandler>.Instance,
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
            new CapturingAdministrativeAlertWriter(),
            NullLogger<PermissionAuthorizationHandler>.Instance,
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
            new CapturingAdministrativeAlertWriter(),
            NullLogger<PermissionAuthorizationHandler>.Instance,
            new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        await RunAsync(handler, new PermissionRequirement(PermissionId));

        Assert.Empty(publisher.PublishedEvents);
    }

    [Fact]
    public async Task Publishes_A_Single_Escalated_Denial_Audit_Event_On_The_Fifth_Denial_In_A_Window()
    {
        // End-to-end through the handler with a real (not faked) PermissionDenialAuditThrottle —
        // denials 1-4 in the same window each hit HandleRequirementAsync but only the first and
        // fifth ever reach the audit publisher (see PermissionDenialAuditThrottleTests for the
        // throttle's own unit coverage of this dedup/escalation logic in isolation).
        var publisher = new FakeAuditEventPublisher();
        var throttle = NewThrottle();
        var handler = new PermissionAuthorizationHandler(
            FakeCurrentUser.Authenticated(UserId, CompanyId.ToString()),
            new FakeAppAuthorizationService(hasPermission: false),
            throttle,
            publisher,
            new CapturingAdministrativeAlertWriter(),
            NullLogger<PermissionAuthorizationHandler>.Instance,
            new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        for (var i = 0; i < 5; i++)
            await RunAsync(handler, new PermissionRequirement(PermissionId));

        Assert.Equal(2, publisher.PublishedEvents.Count);

        var first = Assert.IsType<PermissionDeniedAuditEvent>(publisher.PublishedEvents[0]);
        Assert.False(first.IsRepeatedEscalation);
        Assert.Equal(1, first.DenialCountInWindow);

        var escalation = Assert.IsType<PermissionDeniedAuditEvent>(publisher.PublishedEvents[1]);
        Assert.True(escalation.IsRepeatedEscalation);
        Assert.Equal(5, escalation.DenialCountInWindow);
    }

    // ADM-03: repeated-denial escalation surfaces a Security administrative alert -----------------

    private static PermissionAuthorizationHandler BuildHandler(
        PermissionDenialAuditThrottle throttle,
        CapturingAdministrativeAlertWriter alertWriter,
        bool hasPermission = false) =>
        new(
            FakeCurrentUser.Authenticated(UserId, CompanyId.ToString()),
            new FakeAppAuthorizationService(hasPermission),
            throttle,
            new FakeAuditEventPublisher(),
            alertWriter,
            NullLogger<PermissionAuthorizationHandler>.Instance,
            new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

    [Fact]
    public async Task Raises_One_Security_Alert_On_The_Escalation_Denial()
    {
        var alertWriter = new CapturingAdministrativeAlertWriter();
        var handler = BuildHandler(NewThrottle(), alertWriter);

        for (var i = 0; i < 5; i++)
            await RunAsync(handler, new PermissionRequirement(PermissionId));

        var command = Assert.Single(alertWriter.Commands);
        Assert.Equal(CompanyId, command.CompanyId);
        Assert.Equal(HR.Infrastructure.Abstractions.AdministrativeAlertCategory.Security, command.Category);
        Assert.Equal($"security:repeated-denial:{UserId}", command.DedupKey);
    }

    [Fact]
    public async Task Does_Not_Raise_An_Alert_For_A_Non_Escalation_Denial()
    {
        var alertWriter = new CapturingAdministrativeAlertWriter();
        var handler = BuildHandler(NewThrottle(), alertWriter);

        await RunAsync(handler, new PermissionRequirement(PermissionId));

        Assert.Empty(alertWriter.Commands);
    }

    [Fact]
    public async Task Does_Not_Raise_An_Alert_When_Permission_Is_Granted()
    {
        var alertWriter = new CapturingAdministrativeAlertWriter();
        var handler = BuildHandler(NewThrottle(), alertWriter, hasPermission: true);

        for (var i = 0; i < 6; i++)
            await RunAsync(handler, new PermissionRequirement(PermissionId));

        Assert.Empty(alertWriter.Commands);
    }
}
