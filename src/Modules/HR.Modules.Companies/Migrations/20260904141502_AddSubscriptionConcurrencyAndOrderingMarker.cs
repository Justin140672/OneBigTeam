using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionConcurrencyAndOrderingMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_applied_stripe_event_created_at",
                schema: "companies",
                table: "customer_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_applied_stripe_event_id",
                schema: "companies",
                table: "customer_subscriptions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "companies",
                table: "customer_subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_applied_stripe_event_created_at",
                schema: "companies",
                table: "customer_subscriptions");

            migrationBuilder.DropColumn(
                name: "last_applied_stripe_event_id",
                schema: "companies",
                table: "customer_subscriptions");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "companies",
                table: "customer_subscriptions");
        }
    }
}
