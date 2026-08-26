using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Tests;

public class EmployeeRoleOverrideTests
{
    private static readonly DateTime Now = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowOffset = new(Now, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_All_Properties()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var assignedBy = Guid.NewGuid();
        var expiresAt = NowOffset.AddDays(30);

        var @override = EmployeeRoleOverride.Create(
            companyId, userId, roleId, EmployeeRoleOverrideType.Grant, "Temporary cover", expiresAt, NowOffset, assignedBy);

        Assert.NotEqual(Guid.Empty, @override.Id);
        Assert.Equal(companyId, @override.CompanyId);
        Assert.Equal(userId, @override.UserId);
        Assert.Equal(roleId, @override.RoleId);
        Assert.Equal(EmployeeRoleOverrideType.Grant, @override.OverrideType);
        Assert.Equal("Temporary cover", @override.Reason);
        Assert.Equal(expiresAt, @override.ExpiresAt);
        Assert.Equal(NowOffset, @override.AssignedAt);
        Assert.Equal(assignedBy, @override.AssignedBy);
    }

    [Fact]
    public void Create_AssignedBy_And_ExpiresAt_Are_Optional()
    {
        var @override = EmployeeRoleOverride.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EmployeeRoleOverrideType.Deny, "Reason", null, NowOffset);

        Assert.Null(@override.AssignedBy);
        Assert.Null(@override.ExpiresAt);
    }

    [Fact]
    public void Create_Generates_Unique_Id_Each_Time()
    {
        var a = EmployeeRoleOverride.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EmployeeRoleOverrideType.Grant, "Reason", null, NowOffset);
        var b = EmployeeRoleOverride.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EmployeeRoleOverrideType.Grant, "Reason", null, NowOffset);

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void IsActive_Is_True_When_ExpiresAt_Is_Null()
    {
        var @override = EmployeeRoleOverride.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EmployeeRoleOverrideType.Grant, "Reason", null, NowOffset);

        Assert.True(@override.IsActive(NowOffset.AddYears(10)));
    }

    [Fact]
    public void IsActive_Is_False_Once_ExpiresAt_Has_Passed()
    {
        var @override = EmployeeRoleOverride.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EmployeeRoleOverrideType.Grant,
            "Reason", NowOffset.AddDays(1), NowOffset);

        Assert.True(@override.IsActive(NowOffset));
        Assert.False(@override.IsActive(NowOffset.AddDays(1)));
        Assert.False(@override.IsActive(NowOffset.AddDays(2)));
    }
}
