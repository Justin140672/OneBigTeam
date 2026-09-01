using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AUD04_AuditActorType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The column was never introduced by an earlier migration (no AddColumn/CreateTable
            // for it exists in the audit migration history), so add it here rather than alter it.
            migrationBuilder.AddColumn<int>(
                name: "actor_type",
                schema: "audit",
                table: "audit_events",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "actor_type",
                schema: "audit",
                table: "audit_events");
        }
    }
}
