using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Companies.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPricingConfigToPlatformSettings : Migration
    {
        private const string DefaultBandsJson =
            "[{\"startEmployee\":1,\"endEmployee\":50,\"pricePerEmployee\":2.00}," +
            "{\"startEmployee\":51,\"endEmployee\":150,\"pricePerEmployee\":1.75}," +
            "{\"startEmployee\":151,\"endEmployee\":null,\"pricePerEmployee\":1.50}]";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pricing_bands_json",
                schema: "companies",
                table: "platform_settings",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<decimal>(
                name: "minimum_monthly_charge_gbp",
                schema: "companies",
                table: "platform_settings",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            // Seed the configurable pricing model onto the existing singleton row (if present) with
            // the established default (1-50 £2.00, 51-150 £1.75, 151+ £1.50, minimum £20.00).
            migrationBuilder.Sql(
                "UPDATE companies.platform_settings " +
                "SET pricing_bands_json = '" + DefaultBandsJson + "'::jsonb, " +
                "    minimum_monthly_charge_gbp = 20.00 " +
                "WHERE id = '00000000-0000-0000-0000-000000000001';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pricing_bands_json",
                schema: "companies",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "minimum_monthly_charge_gbp",
                schema: "companies",
                table: "platform_settings");
        }
    }
}
