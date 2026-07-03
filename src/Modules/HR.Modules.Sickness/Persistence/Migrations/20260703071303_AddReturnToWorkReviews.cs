using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Sickness.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnToWorkReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "return_to_work_reviews",
                schema: "sickness",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sickness_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_to_work_reviews", x => x.id);
                    table.ForeignKey(
                        name: "FK_return_to_work_reviews_sickness_records_sickness_record_id",
                        column: x => x.sickness_record_id,
                        principalSchema: "sickness",
                        principalTable: "sickness_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_return_to_work_reviews_company_id",
                schema: "sickness",
                table: "return_to_work_reviews",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_return_to_work_reviews_company_id_status",
                schema: "sickness",
                table: "return_to_work_reviews",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_return_to_work_reviews_sickness_record_id",
                schema: "sickness",
                table: "return_to_work_reviews",
                column: "sickness_record_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "return_to_work_reviews",
                schema: "sickness");
        }
    }
}
