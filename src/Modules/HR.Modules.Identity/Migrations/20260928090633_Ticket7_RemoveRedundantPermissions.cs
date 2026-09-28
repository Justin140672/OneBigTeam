using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class Ticket7_RemoveRedundantPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ticket 7: Remove candidate:view permission (id 000...028) as it is functionally redundant
            // with recruitment:manage. Both permissions are granted to the Recruiter role only, and
            // recruitment:manage is the authoritative permission for all recruiter candidate operations.
            // All endpoints using "candidate:view" policy have been migrated to "recruitment:manage".

            // Remove the role_permission grant: Recruiter + candidate:view
            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "role_id", "permission_id" },
                keyValues: new object[] { new Guid("00000000-0000-0000-0000-000000000003"), new Guid("00000000-0000-0000-0001-000000000028") },
                schema: "identity");

            // Remove the permission itself
            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000028"),
                schema: "identity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the permission
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "name", "created_at" },
                values: new object[] { new Guid("00000000-0000-0000-0001-000000000028"), "candidate.view", new DateTimeOffset(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0)) },
                schema: "identity");

            // Restore the role_permission grant
            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "role_id", "permission_id" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000003"), new Guid("00000000-0000-0000-0001-000000000028") },
                schema: "identity");
        }
    }
}
