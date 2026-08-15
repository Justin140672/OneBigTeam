using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerSubscriptionDeletionScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deletion_scheduled_at",
                schema: "companies",
                table: "customer_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deletion_scheduled_by",
                schema: "companies",
                table: "customer_subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deletion_cancelled_at",
                schema: "companies",
                table: "customer_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deletion_executed_at",
                schema: "companies",
                table: "customer_subscriptions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deletion_scheduled_at",
                schema: "companies",
                table: "customer_subscriptions");

            migrationBuilder.DropColumn(
                name: "deletion_scheduled_by",
                schema: "companies",
                table: "customer_subscriptions");

            migrationBuilder.DropColumn(
                name: "deletion_cancelled_at",
                schema: "companies",
                table: "customer_subscriptions");

            migrationBuilder.DropColumn(
                name: "deletion_executed_at",
                schema: "companies",
                table: "customer_subscriptions");
        }
    }
}
