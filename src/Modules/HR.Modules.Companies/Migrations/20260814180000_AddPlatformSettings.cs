using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_settings",
                schema: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trial_length_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 14),
                    default_monthly_price_gbp = table.Column<decimal>(type: "numeric(10,2)", nullable: false, defaultValue: 0m),
                    support_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false, defaultValue: "support@hrplatform.com"),
                    maintenance_mode_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    maintenance_mode_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    feature_flags_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_settings", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_settings",
                schema: "companies");
        }
    }
}
