using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class CreatePlatformSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            // Drop existing tables if they exist (handles case where old migration created them)
            migrationBuilder.Sql("DROP TABLE IF EXISTS platform.customer_database_assignments CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS platform.idempotency_keys CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS platform.platform_metrics_snapshots CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS platform.platform_settings CASCADE;");

            migrationBuilder.CreateTable(
                name: "customer_database_assignments",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    database_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    schema_oid = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_database_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "platform",
                columns: table => new
                {
                    operation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    response_status_code = table.Column<int>(type: "integer", nullable: false),
                    response_body_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => new { x.operation_id, x.company_id, x.actor_id, x.key });
                });

            migrationBuilder.CreateTable(
                name: "platform_metrics_snapshots",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    active_companies = table.Column<int>(type: "integer", nullable: false),
                    active_users = table.Column<int>(type: "integer", nullable: false),
                    storage_consumed_bytes = table.Column<long>(type: "bigint", nullable: false),
                    background_jobs_succeeded_total = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_metrics_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_settings",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trial_length_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 14),
                    default_monthly_price_gbp = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    support_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false, defaultValue: "support@hrplatform.com"),
                    maintenance_mode_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    maintenance_mode_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    feature_flags_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    pricing_bands_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    minimum_monthly_charge_gbp = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_database_assignments_company_id",
                schema: "platform",
                table: "customer_database_assignments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_database_assignments_database_key",
                schema: "platform",
                table: "customer_database_assignments",
                column: "database_key");

            migrationBuilder.CreateIndex(
                name: "ix_customer_database_assignments_status_company_id",
                schema: "platform",
                table: "customer_database_assignments",
                columns: new[] { "status", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                schema: "platform",
                table: "idempotency_keys",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_platform_metrics_snapshots_snapshot_date",
                schema: "platform",
                table: "platform_metrics_snapshots",
                column: "snapshot_date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_database_assignments",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "platform_metrics_snapshots",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "platform_settings",
                schema: "platform");
        }
    }
}
