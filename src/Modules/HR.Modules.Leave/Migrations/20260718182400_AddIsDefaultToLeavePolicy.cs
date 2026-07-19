using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddIsDefaultToLeavePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_leave_policies_company_id",
                schema: "leave",
                table: "leave_policies");

            migrationBuilder.AddColumn<bool>(
                name: "is_default",
                schema: "leave",
                table: "leave_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: for every company that has at least one leave policy but none marked as
            // default (true for all pre-existing/seeded data since this column is new), mark the
            // company's oldest active policy as default, or oldest policy overall if none are active.
            migrationBuilder.Sql(@"
                WITH ranked_policies AS (
                    SELECT
                        id,
                        company_id,
                        ROW_NUMBER() OVER (
                            PARTITION BY company_id
                            ORDER BY is_active DESC, created_at ASC
                        ) AS rn
                    FROM leave.leave_policies
                ),
                companies_without_default AS (
                    SELECT company_id
                    FROM leave.leave_policies
                    GROUP BY company_id
                    HAVING BOOL_OR(is_default) = FALSE
                )
                UPDATE leave.leave_policies AS lp
                SET is_default = TRUE
                FROM ranked_policies AS rp
                WHERE lp.id = rp.id
                  AND rp.rn = 1
                  AND rp.company_id IN (SELECT company_id FROM companies_without_default);
            ");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policies_company_id_is_default",
                schema: "leave",
                table: "leave_policies",
                column: "company_id",
                unique: true,
                filter: "is_default");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_leave_policies_company_id_is_default",
                schema: "leave",
                table: "leave_policies");

            migrationBuilder.DropColumn(
                name: "is_default",
                schema: "leave",
                table: "leave_policies");

            migrationBuilder.CreateIndex(
                name: "IX_leave_policies_company_id",
                schema: "leave",
                table: "leave_policies",
                column: "company_id");
        }
    }
}
