using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "leave");

            migrationBuilder.CreateTable(
                name: "leave_balances",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_year = table.Column<int>(type: "integer", nullable: false),
                    entitlement_days = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    used_days = table.Column<decimal>(type: "numeric(6,2)", nullable: false, defaultValue: 0m),
                    adjustment_days = table.Column<decimal>(type: "numeric(6,2)", nullable: false, defaultValue: 0m),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leave_balances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leave_policies",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    carry_over_days = table.Column<int>(type: "integer", nullable: false),
                    allow_negative_balance = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leave_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leave_requests",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_days = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reviewed_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leave_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leave_types",
                schema: "leave",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    default_entitlement_days = table.Column<int>(type: "integer", nullable: false),
                    accrual_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    behaviour = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leave_types", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_leave_balances_company_id_employee_id",
                schema: "leave",
                table: "leave_balances",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "IX_leave_balances_company_id_employee_id_leave_type_id_policy_~",
                schema: "leave",
                table: "leave_balances",
                columns: new[] { "company_id", "employee_id", "leave_type_id", "policy_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_leave_balances_company_id_leave_type_id",
                schema: "leave",
                table: "leave_balances",
                columns: new[] { "company_id", "leave_type_id" });

            migrationBuilder.CreateIndex(
                name: "IX_leave_policies_company_id",
                schema: "leave",
                table: "leave_policies",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_leave_requests_company_id",
                schema: "leave",
                table: "leave_requests",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_leave_requests_company_id_employee_id",
                schema: "leave",
                table: "leave_requests",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "IX_leave_requests_company_id_status",
                schema: "leave",
                table: "leave_requests",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_leave_requests_employee_id",
                schema: "leave",
                table: "leave_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "IX_leave_requests_leave_type_id",
                schema: "leave",
                table: "leave_requests",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_leave_types_company_id",
                schema: "leave",
                table: "leave_types",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_leave_types_company_id_code",
                schema: "leave",
                table: "leave_types",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leave_balances",
                schema: "leave");

            migrationBuilder.DropTable(
                name: "leave_policies",
                schema: "leave");

            migrationBuilder.DropTable(
                name: "leave_requests",
                schema: "leave");

            migrationBuilder.DropTable(
                name: "leave_types",
                schema: "leave");
        }
    }
}
