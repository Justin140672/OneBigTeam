using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.Sql(
                """
                INSERT INTO companies.company_branding (
                    company_id,
                    primary_logo_url,
                    small_logo_url,
                    email_logo_url,
                    primary_color,
                    secondary_color,
                    accent_color,
                    created_at,
                    updated_at)
                SELECT
                    c.id,
                    NULL,
                    NULL,
                    NULL,
                    '#0055AA',
                    '#1F2937',
                    '#0EA5E9',
                    NOW(),
                    NOW()
                FROM companies.companies AS c
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM companies.company_branding AS branding
                    WHERE branding.company_id = c.id
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_branding",
                schema: "companies");
        }
    }
}
