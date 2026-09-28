using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class Ticket6_AddSupportRequestPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ticket 6: Add support:request permission for employee self-service support submission.
            // Keep support:manage for HR Administrator queue operations only.
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "name", "created_at" },
                values: new object[] { new Guid("00000000-0000-0000-0001-000000000047"), "support.request", new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0)) },
                schema: "identity");

            // Grant support:request to Employee role
            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "role_id", "permission_id" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), new Guid("00000000-0000-0000-0001-000000000047") },
                schema: "identity");

            // Grant support:request to HR Administrator role (so admins can submit their own support requests)
            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "role_id", "permission_id" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000004"), new Guid("00000000-0000-0000-0001-000000000047") },
                schema: "identity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the role_permission grants
            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "role_id", "permission_id" },
                keyValues: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), new Guid("00000000-0000-0000-0001-000000000047") },
                schema: "identity");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "role_id", "permission_id" },
                keyValues: new object[] { new Guid("00000000-0000-0000-0000-000000000004"), new Guid("00000000-0000-0000-0001-000000000047") },
                schema: "identity");

            // Remove the permission
            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000047"),
                schema: "identity");
        }
    }
}
