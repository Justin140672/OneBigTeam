using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerBillingSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_billing_snapshots",
                schema: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    active_employees = table.Column<int>(type: "integer", nullable: false),
                    future_starters = table.Column<int>(type: "integer", nullable: false),
                    leavers = table.Column<int>(type: "integer", nullable: false),
                    chargeable_employees = table.Column<int>(type: "integer", nullable: false),
                    price_per_employee = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    discounts = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    monthly_total = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_billing_snapshots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_billing_snapshots_company_id",
                schema: "companies",
                table: "customer_billing_snapshots",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_billing_snapshots_company_id_computed_at",
                schema: "companies",
                table: "customer_billing_snapshots",
                columns: new[] { "company_id", "computed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_billing_snapshots",
                schema: "companies");
        }
    }
}
