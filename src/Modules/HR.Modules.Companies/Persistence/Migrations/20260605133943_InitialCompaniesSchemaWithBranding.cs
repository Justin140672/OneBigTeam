using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCompaniesSchemaWithBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "companies");

            migrationBuilder.AlterColumn<string>(
                name: "working_week",
                schema: "companies",
                table: "company_settings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateTable(
                name: "company_branding",
                schema: "companies",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    primary_logo_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    small_logo_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    email_logo_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    primary_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    secondary_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    accent_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_branding", x => x.company_id);
                    table.CheckConstraint("CK_company_branding_accent_color", "accent_color ~ '^#[0-9A-Fa-f]{6}$'");
                    table.CheckConstraint("CK_company_branding_primary_color", "primary_color ~ '^#[0-9A-Fa-f]{6}$'");
                    table.CheckConstraint("CK_company_branding_secondary_color", "secondary_color ~ '^#[0-9A-Fa-f]{6}$'");
                    table.ForeignKey(
                        name: "FK_company_branding_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "companies",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_branding",
                schema: "companies");

            migrationBuilder.AlterColumn<int>(
                name: "working_week",
                schema: "companies",
                table: "company_settings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_company_id",
                schema: "companies",
                table: "outbox_messages",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_status_created_at",
                schema: "companies",
                table: "outbox_messages",
                columns: new[] { "status", "created_at" });
        }
    }
}
