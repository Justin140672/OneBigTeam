using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateFieldsToEmailDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "email_body",
                schema: "notifications",
                table: "email_deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_subject",
                schema: "notifications",
                table: "email_deliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "template_version",
                schema: "notifications",
                table: "email_deliveries",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "email_body",
                schema: "notifications",
                table: "email_deliveries");

            migrationBuilder.DropColumn(
                name: "email_subject",
                schema: "notifications",
                table: "email_deliveries");

            migrationBuilder.DropColumn(
                name: "template_version",
                schema: "notifications",
                table: "email_deliveries");
        }
    }
}
