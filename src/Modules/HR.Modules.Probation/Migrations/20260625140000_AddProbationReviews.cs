using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Probation.Migrations
{
    /// <inheritdoc />
    public partial class AddProbationReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "probation_reviews",
                schema: "probation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    probation_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_probation_reviews", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_probation_reviews_company_id",
                schema: "probation",
                table: "probation_reviews",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_probation_reviews_probation_record_id",
                schema: "probation",
                table: "probation_reviews",
                column: "probation_record_id");

            migrationBuilder.CreateIndex(
                name: "IX_probation_reviews_company_id_status",
                schema: "probation",
                table: "probation_reviews",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_probation_reviews_company_id_due_date",
                schema: "probation",
                table: "probation_reviews",
                columns: new[] { "company_id", "due_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "probation_reviews",
                schema: "probation");
        }
    }
}
