using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Sickness.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSicknessEvidenceRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sickness_evidence_requests",
                schema: "sickness",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sickness_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fulfilled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sickness_evidence_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_sickness_evidence_requests_sickness_records_sickness_record_id",
                        column: x => x.sickness_record_id,
                        principalSchema: "sickness",
                        principalTable: "sickness_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sickness_evidence_requests_company_id",
                schema: "sickness",
                table: "sickness_evidence_requests",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_sickness_evidence_requests_sickness_record_id",
                schema: "sickness",
                table: "sickness_evidence_requests",
                column: "sickness_record_id");

            migrationBuilder.CreateIndex(
                name: "IX_sickness_evidence_requests_company_id_status",
                schema: "sickness",
                table: "sickness_evidence_requests",
                columns: new[] { "company_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sickness_evidence_requests",
                schema: "sickness");
        }
    }
}
