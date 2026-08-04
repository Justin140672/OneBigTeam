using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Support.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "support");

            migrationBuilder.CreateTable(
                name: "support_attachments",
                schema: "support",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    support_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_attachments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "support_notification_attempts",
                schema: "support",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    support_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    recipient_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_notification_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "support_requests",
                schema: "support",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    page_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    browser = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    app_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    include_diagnostics = table.Column<bool>(type: "boolean", nullable: false),
                    diagnostics_json = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "support_response_attachments",
                schema: "support",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    support_response_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_response_attachments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "support_responses",
                schema: "support",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    support_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_staff_response = table.Column<bool>(type: "boolean", nullable: false),
                    body_html = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_responses", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_support_attachments_company_id",
                schema: "support",
                table: "support_attachments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_support_attachments_support_request_id",
                schema: "support",
                table: "support_attachments",
                column: "support_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_support_notification_attempts_company_id",
                schema: "support",
                table: "support_notification_attempts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_support_notification_attempts_status",
                schema: "support",
                table: "support_notification_attempts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_support_notification_attempts_support_request_id",
                schema: "support",
                table: "support_notification_attempts",
                column: "support_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_support_requests_company_id",
                schema: "support",
                table: "support_requests",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_support_requests_company_id_status",
                schema: "support",
                table: "support_requests",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_support_requests_created_at",
                schema: "support",
                table: "support_requests",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_support_requests_reference_number",
                schema: "support",
                table: "support_requests",
                column: "reference_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_support_requests_status",
                schema: "support",
                table: "support_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_support_response_attachments_company_id",
                schema: "support",
                table: "support_response_attachments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_support_response_attachments_support_response_id",
                schema: "support",
                table: "support_response_attachments",
                column: "support_response_id");

            migrationBuilder.CreateIndex(
                name: "IX_support_responses_company_id",
                schema: "support",
                table: "support_responses",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_support_responses_support_request_id",
                schema: "support",
                table: "support_responses",
                column: "support_request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_attachments",
                schema: "support");

            migrationBuilder.DropTable(
                name: "support_notification_attempts",
                schema: "support");

            migrationBuilder.DropTable(
                name: "support_requests",
                schema: "support");

            migrationBuilder.DropTable(
                name: "support_response_attachments",
                schema: "support");

            migrationBuilder.DropTable(
                name: "support_responses",
                schema: "support");
        }
    }
}
