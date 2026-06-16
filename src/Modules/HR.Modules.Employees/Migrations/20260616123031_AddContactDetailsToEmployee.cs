using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddContactDetailsToEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address_line1",
                schema: "employees",
                table: "employees",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_line2",
                schema: "employees",
                table: "employees",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                schema: "employees",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "country",
                schema: "employees",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "county",
                schema: "employees",
                table: "employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "home_phone",
                schema: "employees",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone_number",
                schema: "employees",
                table: "employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "post_code",
                schema: "employees",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address_line1",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "address_line2",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "city",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "country",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "county",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "home_phone",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "phone_number",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "post_code",
                schema: "employees",
                table: "employees");
        }
    }
}
