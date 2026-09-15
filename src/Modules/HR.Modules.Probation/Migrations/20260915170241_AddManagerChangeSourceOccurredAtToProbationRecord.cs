using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Probation.Migrations
{
    /// <inheritdoc />
    public partial class AddManagerChangeSourceOccurredAtToProbationRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "manager_change_source_occurred_at",
                schema: "probation",
                table: "probation_records",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "manager_change_source_occurred_at",
                schema: "probation",
                table: "probation_records");
        }
    }
}
