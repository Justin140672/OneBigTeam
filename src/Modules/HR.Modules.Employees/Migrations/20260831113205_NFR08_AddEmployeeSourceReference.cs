using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class NFR08_AddEmployeeSourceReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_reference",
                schema: "employees",
                table: "employees",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_company_id_source_reference",
                schema: "employees",
                table: "employees",
                columns: new[] { "company_id", "source_reference" },
                unique: true,
                filter: "source_reference IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_employees_company_id_source_reference",
                schema: "employees",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "source_reference",
                schema: "employees",
                table: "employees");
        }
    }
}
