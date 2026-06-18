using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueDateExpiryDateToEmployeeDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "documents",
                table: "employee_documents",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "issue_date",
                schema: "documents",
                table: "employee_documents",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "documents",
                table: "employee_documents");

            migrationBuilder.DropColumn(
                name: "issue_date",
                schema: "documents",
                table: "employee_documents");
        }
    }
}
