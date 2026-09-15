using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddToilExpiryDedupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_toil_transactions_related_transaction_id",
                schema: "leave",
                table: "toil_transactions");

            migrationBuilder.CreateIndex(
                name: "ix_toil_transactions_related_transaction_id_expired_unique",
                schema: "leave",
                table: "toil_transactions",
                column: "related_transaction_id",
                unique: true,
                filter: "type = 'Expired'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_toil_transactions_related_transaction_id_expired_unique",
                schema: "leave",
                table: "toil_transactions");

            migrationBuilder.CreateIndex(
                name: "IX_toil_transactions_related_transaction_id",
                schema: "leave",
                table: "toil_transactions",
                column: "related_transaction_id");
        }
    }
}
