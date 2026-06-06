using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Tests;

public class EmployeeRoleOverrideTests
{
    private static readonly DateTime Now = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowOffset = new(Now, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_All_Properties()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var assignedBy = Guid.NewGuid();

        var @override = EmployeeRoleOverride.Create(userId, roleId, EmployeeRoleOverrideType.Grant, NowOffset, assignedBy);

        Assert.NotEqual(Guid.Empty, @override.Id);
        Assert.Equal(userId, @override.UserId);
        Assert.Equal(roleId, @override.RoleId);
        Assert.Equal(EmployeeRoleOverrideType.Grant, @override.OverrideType);
        Assert.Equal(NowOffset, @override.AssignedAt);
        Assert.Equal(assignedBy, @override.AssignedBy);
    }

    [Fact]
    public void Create_AssignedBy_Is_Optional()
    {
        var @override = EmployeeRoleOverride.Create(
            Guid.NewGuid(), Guid.NewGuid(), EmployeeRoleOverrideType.Deny, NowOffset);

        Assert.Null(@override.AssignedBy);
    }

    [Fact]
    public void Create_Generates_Unique_Id_Each_Time()
    {
        var a = EmployeeRoleOverride.Create(Guid.NewGuid(), Guid.NewGuid(), EmployeeRoleOverrideType.Grant, NowOffset);
        var b = EmployeeRoleOverride.Create(Guid.NewGuid(), Guid.NewGuid(), EmployeeRoleOverrideType.Grant, NowOffset);

        Assert.NotEqual(a.Id, b.Id);
    }
}
