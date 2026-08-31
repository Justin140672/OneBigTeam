using HR.Modules.Onboarding.Services;
using HR.Modules.Onboarding.Tests.Infrastructure;

namespace HR.Modules.Onboarding.Tests;

/// <summary>
/// DSH-02: <c>OnboardingResourceAuthorizer.CanViewManagerTeamAsync</c> gates the browser-supplied
/// <c>{managerId}</c> route value on GetTeamOnboarding. The caller may view that manager's team only
/// if they ARE that manager, sit ABOVE them in the reporting tree (managerId is one of the caller's
/// descendants), or hold HR administrator access. See
/// specifications/architecture/11-manager-hierarchy-scope.md.
/// </summary>
public class OnboardingResourceAuthorizerTests
{
    // Mirrors HR.Modules.Onboarding.Services.OnboardingResourceAuthorizer.HrAdministratorRoleId.
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");
    private static readonly Guid ManagerRoleId = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static OnboardingResourceAuthorizer Build(
        FakeRoleAuthorizationService? roles = null,
        FakeDirectReportsReader? reader = null) =>
        new(roles ?? new FakeRoleAuthorizationService(), reader ?? FakeDirectReportsReader.WithHierarchy());

    [Fact]
    public async Task Allows_The_Manager_Themselves()
    {
        var manager = Guid.NewGuid();

        var result = await Build().CanViewManagerTeamAsync(CompanyId, manager, manager, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task Allows_Direct_Manager_Of_The_Requested_Manager()
    {
        var seniorManager = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy((seniorManager, manager));

        var result = await Build(reader: reader)
            .CanViewManagerTeamAsync(CompanyId, seniorManager, manager, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task Allows_Skip_Level_Manager_Of_The_Requested_Manager()
    {
        var director = Guid.NewGuid();
        var seniorManager = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy(
            (director, seniorManager),
            (seniorManager, manager));

        var result = await Build(reader: reader)
            .CanViewManagerTeamAsync(CompanyId, director, manager, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task Allows_Hr_Administrator_Regardless_Of_Hierarchy()
    {
        var caller = Guid.NewGuid();
        var manager = Guid.NewGuid();

        var result = await Build(roles: new FakeRoleAuthorizationService(HrAdministratorRoleId))
            .CanViewManagerTeamAsync(CompanyId, caller, manager, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task Denies_A_Peer_Manager()
    {
        var director = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var peerManager = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy(
            (director, manager),
            (director, peerManager));

        var result = await Build(roles: new FakeRoleAuthorizationService(ManagerRoleId), reader: reader)
            .CanViewManagerTeamAsync(CompanyId, peerManager, manager, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task Denies_An_Unrelated_Manager()
    {
        var manager = Guid.NewGuid();
        var unrelatedManager = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy((unrelatedManager, Guid.NewGuid()));

        var result = await Build(roles: new FakeRoleAuthorizationService(ManagerRoleId), reader: reader)
            .CanViewManagerTeamAsync(CompanyId, unrelatedManager, manager, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task Denies_A_Subordinate_Viewing_Their_Own_Manager_Bottom_Up()
    {
        var manager = Guid.NewGuid();
        var report = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy((manager, report));

        var result = await Build(reader: reader)
            .CanViewManagerTeamAsync(CompanyId, report, manager, CancellationToken.None);

        Assert.False(result);
    }
}
