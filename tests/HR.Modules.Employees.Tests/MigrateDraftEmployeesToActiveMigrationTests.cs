using HR.Modules.Employees.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HR.Modules.Employees.Tests;

public class MigrateDraftEmployeesToActiveMigrationTests
{
    [Fact]
    public void Up_Updates_Only_Draft_Employee_Rows_To_Active()
    {
        var operations = new MigrateDraftEmployeesToActive().UpOperations;

        var sql = Assert.IsType<SqlOperation>(Assert.Single(operations));
        Assert.Contains("UPDATE employees.employees", sql.Sql);
        Assert.Contains("SET status = 'Active'", sql.Sql);
        Assert.Contains("WHERE status = 'Draft'", sql.Sql);
    }

    [Fact]
    public void Down_Does_Nothing_Because_Draft_Is_Retired()
    {
        Assert.Empty(new MigrateDraftEmployeesToActive().DownOperations);
    }
}
