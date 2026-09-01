using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalHoldToCustomerSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "legal_hold_placed_at",
                schema: "companies",
                table: "customer_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "legal_hold_placed_by",
                schema: "companies",
                table: "customer_subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "legal_hold_reason",
                schema: "companies",
                table: "customer_subscriptions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "legal_hold_placed_at",
                schema: "companies",
                table: "customer_subscriptions");

            migrationBuilder.DropColumn(
                name: "legal_hold_placed_by",
                schema: "companies",
                table: "customer_subscriptions");

            migrationBuilder.DropColumn(
                name: "legal_hold_reason",
                schema: "companies",
                table: "customer_subscriptions");
        }
    }
}
