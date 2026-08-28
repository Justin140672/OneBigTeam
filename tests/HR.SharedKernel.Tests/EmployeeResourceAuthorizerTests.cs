using HR.SharedKernel.Authorization;

namespace HR.SharedKernel.Tests;

public class EmployeeResourceAuthorizerTests
{
    private static EmployeeResourceAuthorizer BuildAuthorizer(
        bool hasCompanyWideAccess = false,
        params Guid[] descendantIds) =>
        new(
            (_, _) => Task.FromResult(hasCompanyWideAccess),
            (_, _, _) => Task.FromResult<IReadOnlyList<Guid>>(descendantIds));

    [Fact]
    public async Task CanAccessAsync_Denies_When_Company_Boundary_Mismatch_Even_With_Company_Wide_Access()
    {
        var authorizer = BuildAuthorizer(hasCompanyWideAccess: true);
        var employeeId = Guid.NewGuid();

        var result = await authorizer.CanAccessAsync(
            Guid.NewGuid(), Guid.NewGuid(), employeeId, employeeId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanAccessAsync_Denies_When_Company_Boundary_Mismatch_Even_When_Caller_Is_Target()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var authorizer = BuildAuthorizer(descendantIds: [targetId]);

        var result = await authorizer.CanAccessAsync(
            companyA, companyB, callerId, targetId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanAccessAsync_Allows_When_Company_Wide_Access_Regardless_Of_Hierarchy_Or_Self()
    {
        var companyId = Guid.NewGuid();
        var authorizer = BuildAuthorizer(hasCompanyWideAccess: true);

        var result = await authorizer.CanAccessAsync(
            companyId, companyId, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None,
            allowSelf: false, allowHierarchy: false);

        Assert.True(result);
    }

    [Fact]
    public async Task CanAccessAsync_Allows_When_Hierarchy_Match_And_AllowHierarchy_True()
    {
        var companyId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var authorizer = BuildAuthorizer(descendantIds: [targetId]);

        var result = await authorizer.CanAccessAsync(
            companyId, companyId, callerId, targetId, CancellationToken.None,
            allowSelf: false, allowHierarchy: true);

        Assert.True(result);
    }

    [Fact]
    public async Task CanAccessAsync_Denies_Hierarchy_Match_When_AllowHierarchy_False()
    {
        var companyId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var authorizer = BuildAuthorizer(descendantIds: [targetId]);

        var result = await authorizer.CanAccessAsync(
            companyId, companyId, callerId, targetId, CancellationToken.None,
            allowSelf: false, allowHierarchy: false);

        Assert.False(result);
    }

    [Fact]
    public async Task CanAccessAsync_Allows_Self_When_AllowSelf_True_And_Not_In_Hierarchy_Or_Company_Wide()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var authorizer = BuildAuthorizer();

        var result = await authorizer.CanAccessAsync(
            companyId, companyId, employeeId, employeeId, CancellationToken.None,
            allowSelf: true, allowHierarchy: true);

        Assert.True(result);
    }

    [Fact]
    public async Task CanAccessAsync_Denies_Self_When_AllowSelf_False()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var authorizer = BuildAuthorizer();

        var result = await authorizer.CanAccessAsync(
            companyId, companyId, employeeId, employeeId, CancellationToken.None,
            allowSelf: false, allowHierarchy: true);

        Assert.False(result);
    }

    [Fact]
    public async Task CanAccessAsync_Denies_When_Caller_Is_Not_Company_Wide_Hierarchy_Or_Self()
    {
        var companyId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var authorizer = BuildAuthorizer(descendantIds: [Guid.NewGuid()]);

        var result = await authorizer.CanAccessAsync(
            companyId, companyId, callerId, targetId, CancellationToken.None);

        Assert.False(result);
    }
}
