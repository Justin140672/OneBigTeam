using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Reporting.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganisationDataExportLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_acquired_at",
                schema: "reporting",
                table: "organisation_data_exports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at",
                schema: "reporting",
                table: "organisation_data_exports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lease_owner_token",
                schema: "reporting",
                table: "organisation_data_exports",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lease_acquired_at",
                schema: "reporting",
                table: "organisation_data_exports");

            migrationBuilder.DropColumn(
                name: "lease_expires_at",
                schema: "reporting",
                table: "organisation_data_exports");

            migrationBuilder.DropColumn(
                name: "lease_owner_token",
                schema: "reporting",
                table: "organisation_data_exports");
        }
    }
}
