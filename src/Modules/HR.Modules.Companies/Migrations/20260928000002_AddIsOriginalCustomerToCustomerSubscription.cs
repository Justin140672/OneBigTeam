using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddIsOriginalCustomerToCustomerSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_original_customer",
                schema: "companies",
                table: "customer_subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill all existing rows to false (all current customers are "new")
            migrationBuilder.Sql(
                @"UPDATE companies.customer_subscriptions
                  SET is_original_customer = false
                  WHERE is_original_customer IS NULL OR is_original_customer = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_original_customer",
                schema: "companies",
                table: "customer_subscriptions");
        }
    }
}
