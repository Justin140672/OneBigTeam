using HR.Modules.Sickness.Services;
using HR.Modules.Sickness.Tests.Infrastructure;

namespace HR.Modules.Sickness.Tests.Services;

/// <summary>
/// DSH-02: <c>SicknessResourceAuthorizer.CanViewManagerTeamAsync</c> gates the browser-supplied
/// <c>{managerId}</c> route value on GetTeamSicknessToday. The caller may view that manager's team
/// only if they ARE that manager, sit ABOVE them in the reporting tree, or hold company-wide
/// (HR administrator / sickness.manage) access. See
/// specifications/architecture/11-manager-hierarchy-scope.md.
/// </summary>
public class SicknessResourceAuthorizerCanViewManagerTeamTests
{
    // Mirrors HR.Modules.Sickness.Services.SicknessResourceAuthorizer.SicknessManagePermissionId.
    private static readonly Guid SicknessManagePermissionId = new("00000000-0000-0000-0001-000000000015");
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static SicknessResourceAuthorizer Build(
        FakePermissionAuthorizationService? perms = null,
        FakeDirectReportsReader? reader = null) =>
        new(perms ?? new FakePermissionAuthorizationService(), reader ?? FakeDirectReportsReader.WithHierarchy());

    [Fact]
    public async Task Allows_The_Manager_Themselves()
    {
        var manager = Guid.NewGuid();

        Assert.True(await Build().CanViewManagerTeamAsync(CompanyId, manager, manager, CancellationToken.None));
    }

    [Fact]
    public async Task Allows_Direct_Manager_Of_The_Requested_Manager()
    {
        var senior = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy((senior, manager));

        Assert.True(await Build(reader: reader)
            .CanViewManagerTeamAsync(CompanyId, senior, manager, CancellationToken.None));
    }

    [Fact]
    public async Task Allows_Skip_Level_Manager_Of_The_Requested_Manager()
    {
        var director = Guid.NewGuid();
        var senior = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy((director, senior), (senior, manager));

        Assert.True(await Build(reader: reader)
            .CanViewManagerTeamAsync(CompanyId, director, manager, CancellationToken.None));
    }

    [Fact]
    public async Task Allows_Hr_Administrator_Regardless_Of_Hierarchy()
    {
        var caller = Guid.NewGuid();
        var manager = Guid.NewGuid();

        Assert.True(await Build(perms: new FakePermissionAuthorizationService(SicknessManagePermissionId))
            .CanViewManagerTeamAsync(CompanyId, caller, manager, CancellationToken.None));
    }

    [Fact]
    public async Task Denies_A_Peer_Manager()
    {
        var director = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy((director, manager), (director, peer));

        Assert.False(await Build(reader: reader)
            .CanViewManagerTeamAsync(CompanyId, peer, manager, CancellationToken.None));
    }

    [Fact]
    public async Task Denies_An_Unrelated_Manager()
    {
        var manager = Guid.NewGuid();
        var unrelated = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy((unrelated, Guid.NewGuid()));

        Assert.False(await Build(reader: reader)
            .CanViewManagerTeamAsync(CompanyId, unrelated, manager, CancellationToken.None));
    }

    [Fact]
    public async Task Denies_A_Subordinate_Viewing_Their_Own_Manager_Bottom_Up()
    {
        var manager = Guid.NewGuid();
        var report = Guid.NewGuid();
        var reader = FakeDirectReportsReader.WithHierarchy((manager, report));

        Assert.False(await Build(reader: reader)
            .CanViewManagerTeamAsync(CompanyId, report, manager, CancellationToken.None));
    }
}
