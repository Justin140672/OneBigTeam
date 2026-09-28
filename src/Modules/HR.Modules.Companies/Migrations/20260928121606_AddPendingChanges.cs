using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_database_assignments",
                schema: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    database_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    schema_oid = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_database_assignments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_database_assignments_company_id",
                schema: "companies",
                table: "customer_database_assignments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_database_assignments_database_key",
                schema: "companies",
                table: "customer_database_assignments",
                column: "database_key");

            migrationBuilder.CreateIndex(
                name: "ix_customer_database_assignments_status_company_id",
                schema: "companies",
                table: "customer_database_assignments",
                columns: new[] { "status", "company_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_database_assignments",
                schema: "companies");
        }
    }
}
