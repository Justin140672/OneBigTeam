using System.Security.Claims;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// OBT-721 workload action provider tests for employee accounts awaiting invitation. HR-only.
/// Identity owns UserInvite directly (see provider xmldoc), so this queries IdentityDbContext
/// in-memory rather than a fake reader.
/// </summary>
public class EmployeeAccountsAwaitingInvitationWorkloadActionProviderTests
{
    private static ClaimsPrincipal AnyCaller() => new(new ClaimsIdentity());

    private static IdentityDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new IdentityDbContext(options);
    }

    [Fact]
    public async Task HrCaller_Sees_Pending_Invitations_CompanyWide()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();

        context.UserInvites.AddRange(
            UserInvite.Create(employeeA, companyId, "a@example.com", DateTimeOffset.UtcNow),
            UserInvite.Create(employeeB, companyId, "b@example.com", DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        var provider = new EmployeeAccountsAwaitingInvitationWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.Equal("Pending Invitation", a.Status));
    }

    [Fact]
    public async Task NonHrCaller_Returns_Empty_Not_Throws()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.UserInvites.Add(UserInvite.Create(Guid.NewGuid(), companyId, "a@example.com", DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        var provider = new EmployeeAccountsAwaitingInvitationWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService());

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Excludes_Claimed_And_Cancelled_Invites()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var claimed = UserInvite.Create(Guid.NewGuid(), companyId, "claimed@example.com", DateTimeOffset.UtcNow);
        claimed.Claim(DateTimeOffset.UtcNow);

        var cancelled = UserInvite.Create(Guid.NewGuid(), companyId, "cancelled@example.com", DateTimeOffset.UtcNow);
        cancelled.Cancel(DateTimeOffset.UtcNow);

        context.UserInvites.AddRange(claimed, cancelled);
        await context.SaveChangesAsync();

        var provider = new EmployeeAccountsAwaitingInvitationWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Distinguishes_Expired_From_Pending_Invitations()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var expiredEmployeeId = Guid.NewGuid();
        var pendingEmployeeId = Guid.NewGuid();

        // Created 10 days ago -> ExpiresAt (now + 7 days) is 3 days in the past.
        var expired = UserInvite.Create(
            expiredEmployeeId, companyId, "expired@example.com", DateTimeOffset.UtcNow.AddDays(-10));
        var pending = UserInvite.Create(
            pendingEmployeeId, companyId, "pending@example.com", DateTimeOffset.UtcNow);

        context.UserInvites.AddRange(expired, pending);
        await context.SaveChangesAsync();

        var provider = new EmployeeAccountsAwaitingInvitationWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        var expiredAction = Assert.Single(result, a => a.EmployeeId == expiredEmployeeId);
        Assert.Equal("Resend Expired Invitation", expiredAction.ActionType);
        Assert.Equal("Invitation Expired", expiredAction.Status);

        var pendingAction = Assert.Single(result, a => a.EmployeeId == pendingEmployeeId);
        Assert.Equal("Awaiting Invitation Acceptance", pendingAction.ActionType);
        Assert.Equal("Pending Invitation", pendingAction.Status);
    }

    [Fact]
    public async Task Maps_ActionCategory_And_DeepLink()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        context.UserInvites.Add(UserInvite.Create(employeeId, companyId, "a@example.com", DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        var provider = new EmployeeAccountsAwaitingInvitationWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Employee Accounts Awaiting Invitation", action.ActionCategory);
        Assert.Equal($"/companies/{companyId}/user-administration/{employeeId}", action.DeepLinkUrl);
    }
}
