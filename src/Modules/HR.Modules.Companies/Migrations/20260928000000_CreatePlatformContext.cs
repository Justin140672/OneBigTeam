using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class CreatePlatformContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the platform schema
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS platform");

            // Create customer_database_assignments table in platform schema
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
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_database_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_database_assignments_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "companies",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.CheckConstraint(
                        name: "CK_customer_database_assignments_status",
                        sql: "status IN ('Pending', 'Active', 'Inactive')");
                });

            // Create indexes for customer_database_assignments
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

            // Migrate platform_settings from companies schema to platform schema
            migrationBuilder.CreateTable(
                name: "platform_settings",
                schema: "platform",
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

            // Copy existing platform_settings from companies schema if any exist
            migrationBuilder.Sql(
                @"INSERT INTO platform.platform_settings
                  (id, trial_length_days, default_monthly_price_gbp, support_email,
                   maintenance_mode_enabled, maintenance_mode_message, feature_flags_json,
                   pricing_bands_json, minimum_monthly_charge_gbp, updated_at, updated_by_user_id)
                  SELECT id, trial_length_days, default_monthly_price_gbp, support_email,
                         maintenance_mode_enabled, maintenance_mode_message, feature_flags_json,
                         COALESCE(pricing_bands_json, '[]'), minimum_monthly_charge_gbp, updated_at, updated_by_user_id
                  FROM companies.platform_settings
                  ON CONFLICT DO NOTHING");

            // Migrate platform_metrics_snapshots from companies schema to platform schema
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
                    background_jobs_succeeded_total = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_metrics_snapshots", x => x.id);
                });

            // Copy existing platform_metrics_snapshots from companies schema if any exist
            migrationBuilder.Sql(
                @"INSERT INTO platform.platform_metrics_snapshots
                  (id, snapshot_date, computed_at, active_companies, active_users,
                   storage_consumed_bytes, background_jobs_succeeded_total)
                  SELECT id, snapshot_date, computed_at, active_companies, active_users,
                         storage_consumed_bytes, background_jobs_succeeded_total
                  FROM companies.platform_metrics_snapshots
                  ON CONFLICT DO NOTHING");

            migrationBuilder.CreateIndex(
                name: "ix_platform_metrics_snapshots_snapshot_date",
                schema: "platform",
                table: "platform_metrics_snapshots",
                column: "snapshot_date",
                unique: true);

            // Create idempotency_records table in platform schema (mirroring companies schema)
            migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_scope_idempotency_key",
                schema: "platform",
                table: "idempotency_records",
                columns: new[] { "scope", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_expires_at",
                schema: "platform",
                table: "idempotency_records",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_database_assignments",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "platform_settings",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "platform_metrics_snapshots",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "idempotency_records",
                schema: "platform");

            migrationBuilder.Sql("DROP SCHEMA IF EXISTS platform");
        }
    }
}
