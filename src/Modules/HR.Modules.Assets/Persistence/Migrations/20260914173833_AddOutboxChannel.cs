using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Assets.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "channel",
                schema: "assets",
                table: "audit_outbox",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "audit");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "channel",
                schema: "assets",
                table: "audit_outbox");
        }
    }
}
