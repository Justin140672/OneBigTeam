using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add the new column nullable first so existing rows can be backfilled from the old
            // is_active column before it is dropped and before the NOT NULL constraint is applied.
            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "companies",
                table: "companies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE companies.companies
                SET status = CASE WHEN is_active THEN 'Active' ELSE 'Deactivated' END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "companies",
                table: "companies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "companies",
                table: "companies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "companies",
                table: "companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE companies.companies
                SET is_active = (status = 'Active');
                """);

            migrationBuilder.DropColumn(
                name: "status",
                schema: "companies",
                table: "companies");
        }
    }
}
