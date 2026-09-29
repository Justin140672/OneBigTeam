using System.Reflection;
using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// IAM-06: sanity checks over <see cref="PolicyCatalog"/> itself, independent of the exhaustive
/// role/policy matrix covered by <see cref="PolicyMatrixTests"/>.
/// </summary>
public class PolicyCatalogTests
{
    [Fact]
    public void Has_Exactly_The_Expected_Number_Of_Policies()
    {
        Assert.Equal(37, PolicyCatalog.PermissionPolicies.Count);
    }

    [Fact]
    public void No_Policy_Name_Is_Null_Or_Whitespace()
    {
        Assert.All(PolicyCatalog.PermissionPolicies.Keys, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    [Fact]
    public void Every_Referenced_Permission_Id_Is_A_Real_Distinct_SystemPermissions_Constant()
    {
        var declaredPermissionIds = typeof(SystemPermissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Guid))
            .Select(f => (Guid)f.GetValue(null)!)
            .ToHashSet();

        // No default/empty Guid ever declared as a permission constant.
        Assert.DoesNotContain(Guid.Empty, declaredPermissionIds);

        foreach (var (policyName, permissionId) in PolicyCatalog.PermissionPolicies)
        {
            Assert.NotEqual(Guid.Empty, permissionId);
            Assert.Contains(permissionId, declaredPermissionIds);
        }
    }

    [Fact]
    public void All_SystemPermissions_Constants_Are_Distinct()
    {
        var declaredPermissionIds = typeof(SystemPermissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Guid))
            .Select(f => (Guid)f.GetValue(null)!)
            .ToList();

        Assert.Equal(declaredPermissionIds.Count, declaredPermissionIds.Distinct().Count());
    }
}
