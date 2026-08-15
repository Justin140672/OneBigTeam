using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformMetricsSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_metrics_snapshots",
                schema: "companies");
        }
    }
}
