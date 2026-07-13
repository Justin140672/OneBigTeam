using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Documents.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedCompanyDocumentPublishedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                schema: "documents",
                table: "shared_company_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "published_by",
                schema: "documents",
                table: "shared_company_documents",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "published_at",
                schema: "documents",
                table: "shared_company_documents");

            migrationBuilder.DropColumn(
                name: "published_by",
                schema: "documents",
                table: "shared_company_documents");
        }
    }
}
