using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedStripeEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processed_stripe_events",
                schema: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stripe_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stripe_subscription_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    applied = table.Column<bool>(type: "boolean", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_stripe_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_processed_stripe_events_stripe_event_id",
                schema: "companies",
                table: "processed_stripe_events",
                column: "stripe_event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_processed_stripe_events_subscription_created",
                schema: "companies",
                table: "processed_stripe_events",
                columns: new[] { "stripe_subscription_id", "event_created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_stripe_events",
                schema: "companies");
        }
    }
}
