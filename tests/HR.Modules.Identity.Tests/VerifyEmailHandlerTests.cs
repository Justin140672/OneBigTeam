using HR.Modules.Identity.Features.VerifyEmail;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

public class VerifyEmailHandlerTests
{
    private static readonly DateTime Now = new(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FakeClock Clock = new(Now);

    private sealed record Dependencies(
        FakeCompanyProvisioner Provisioner,
        FakeAuditEventPublisher AuditEventPublisher);

    private static VerifyEmailHandler BuildHandler(
        Dependencies dependencies, Guid? userId, Guid? companyId) =>
        new(
            new FakeCurrentUser(userId),
            companyId is null ? FakeCurrentTenant.None : FakeCurrentTenant.For(companyId.Value),
            dependencies.Provisioner,
            dependencies.AuditEventPublisher,
            Clock);

    private static Dependencies BuildDependencies() => new(
        new FakeCompanyProvisioner(),
        new FakeAuditEventPublisher());

    [Fact]
    public async Task HandleAsync_Activates_Company_And_Publishes_Both_Audit_Events_On_First_Verification()
    {
        var deps = BuildDependencies();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var handler = BuildHandler(deps, userId, companyId);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value!.UserId);
        Assert.Equal(companyId, result.Value.CompanyId);

        Assert.Equal(1, deps.Provisioner.ActivateCompanyCallCount);
        Assert.Contains(companyId, deps.Provisioner.ActivatedCompanyIds);

        Assert.Equal(2, deps.AuditEventPublisher.PublishedEvents.Count);
        var succeededEvent = Assert.IsType<EmailVerificationSucceededAuditEvent>(deps.AuditEventPublisher.PublishedEvents[0]);
        Assert.Equal(companyId, succeededEvent.CompanyId);
        Assert.Equal(userId, succeededEvent.UserId);

        var activatedEvent = Assert.IsType<CompanyActivatedAuditEvent>(deps.AuditEventPublisher.PublishedEvents[1]);
        Assert.Equal(companyId, activatedEvent.CompanyId);
    }

    [Fact]
    public async Task HandleAsync_Is_Idempotent_When_Company_Is_Already_Active()
    {
        var deps = BuildDependencies();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        deps.Provisioner.ActiveCompanyIds.Add(companyId);
        var handler = BuildHandler(deps, userId, companyId);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value!.UserId);
        Assert.Equal(companyId, result.Value.CompanyId);

        Assert.Equal(0, deps.Provisioner.ActivateCompanyCallCount);
        Assert.Empty(deps.Provisioner.ActivatedCompanyIds);
        Assert.Empty(deps.AuditEventPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Returns_InvalidOrExpired_When_No_UserId_Resolved()
    {
        var deps = BuildDependencies();
        var handler = BuildHandler(deps, userId: null, companyId: Guid.NewGuid());

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_or_expired", result.Error.Code);
        Assert.Equal(0, deps.Provisioner.ActivateCompanyCallCount);
        Assert.Empty(deps.AuditEventPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Returns_InvalidOrExpired_When_No_TenantId_Resolved()
    {
        var deps = BuildDependencies();
        var handler = BuildHandler(deps, userId: Guid.NewGuid(), companyId: null);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_or_expired", result.Error.Code);
        Assert.Equal(0, deps.Provisioner.ActivateCompanyCallCount);
        Assert.Empty(deps.AuditEventPublisher.PublishedEvents);
    }
}
