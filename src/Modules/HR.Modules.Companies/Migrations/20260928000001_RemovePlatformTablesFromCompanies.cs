using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class RemovePlatformTablesFromCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop platform_metrics_snapshots from companies schema
            migrationBuilder.DropIndex(
                name: "ix_platform_metrics_snapshots_snapshot_date",
                schema: "companies",
                table: "platform_metrics_snapshots");

            migrationBuilder.DropTable(
                name: "platform_metrics_snapshots",
                schema: "companies");

            // Drop platform_settings from companies schema
            migrationBuilder.DropTable(
                name: "platform_settings",
                schema: "companies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreate platform_settings in companies schema
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
                    pricing_bands_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    minimum_monthly_charge_gbp = table.Column<decimal>(type: "numeric(10,2)", nullable: false, defaultValue: 0m),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_settings", x => x.id);
                });

            // Recreate platform_metrics_snapshots in companies schema
            migrationBuilder.CreateTable(
                name: "platform_metrics_snapshots",
                schema: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    active_companies = table.Column<int>(type: "integer", nullable: false),
                    active_users = table.Column<int>(type: "integer", nullable: false),
                    storage_consumed_bytes = table.Column<long>(type: "bigint", nullable: false),
                    background_jobs_succeeded_total = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_metrics_snapshots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_metrics_snapshots_snapshot_date",
                schema: "companies",
                table: "platform_metrics_snapshots",
                column: "snapshot_date",
                unique: true);

            // Copy data back from platform schema if needed
            migrationBuilder.Sql(
                @"INSERT INTO companies.platform_settings
                  (id, trial_length_days, default_monthly_price_gbp, support_email,
                   maintenance_mode_enabled, maintenance_mode_message, feature_flags_json,
                   pricing_bands_json, minimum_monthly_charge_gbp, updated_at, updated_by_user_id)
                  SELECT id, trial_length_days, default_monthly_price_gbp, support_email,
                         maintenance_mode_enabled, maintenance_mode_message, feature_flags_json,
                         pricing_bands_json, minimum_monthly_charge_gbp, updated_at, updated_by_user_id
                  FROM platform.platform_settings
                  ON CONFLICT DO NOTHING");

            migrationBuilder.Sql(
                @"INSERT INTO companies.platform_metrics_snapshots
                  (id, snapshot_date, computed_at, active_companies, active_users,
                   storage_consumed_bytes, background_jobs_succeeded_total)
                  SELECT id, snapshot_date, computed_at, active_companies, active_users,
                         storage_consumed_bytes, background_jobs_succeeded_total
                  FROM platform.platform_metrics_snapshots
                  ON CONFLICT DO NOTHING");
        }
    }
}
