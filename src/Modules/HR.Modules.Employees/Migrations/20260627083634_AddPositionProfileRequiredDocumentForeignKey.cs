using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionProfileRequiredDocumentForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_position_profile_required_documents_position_profiles_posit~",
                schema: "employees",
                table: "position_profile_required_documents",
                column: "position_profile_id",
                principalSchema: "employees",
                principalTable: "position_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_position_profile_required_documents_position_profiles_posit~",
                schema: "employees",
                table: "position_profile_required_documents");
        }
    }
}
