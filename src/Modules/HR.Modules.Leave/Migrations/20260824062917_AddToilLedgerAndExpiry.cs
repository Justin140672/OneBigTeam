using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Leave.Migrations
{
    /// <inheritdoc />
    public partial class AddToilLedgerAndExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "awarded_by_employee_id",
                schema: "leave",
                table: "toil_transactions",
                newName: "actor_employee_id");

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "leave",
                table: "toil_transactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "expires_on",
                schema: "leave",
                table: "toil_transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "related_transaction_id",
                schema: "leave",
                table: "toil_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reverses_transaction_id",
                schema: "leave",
                table: "toil_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_leave_request_id",
                schema: "leave",
                table: "toil_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "type",
                schema: "leave",
                table: "toil_transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Earned");

            migrationBuilder.AddColumn<bool>(
                name: "allow_negative_toil_balance",
                schema: "leave",
                table: "leave_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "toil_expiry_days",
                schema: "leave",
                table: "leave_types",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_toil_transactions_related_transaction_id",
                schema: "leave",
                table: "toil_transactions",
                column: "related_transaction_id");

            migrationBuilder.CreateIndex(
                name: "IX_toil_transactions_source_leave_request_id",
                schema: "leave",
                table: "toil_transactions",
                column: "source_leave_request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_toil_transactions_related_transaction_id",
                schema: "leave",
                table: "toil_transactions");

            migrationBuilder.DropIndex(
                name: "IX_toil_transactions_source_leave_request_id",
                schema: "leave",
                table: "toil_transactions");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "leave",
                table: "toil_transactions");

            migrationBuilder.DropColumn(
                name: "expires_on",
                schema: "leave",
                table: "toil_transactions");

            migrationBuilder.DropColumn(
                name: "related_transaction_id",
                schema: "leave",
                table: "toil_transactions");

            migrationBuilder.DropColumn(
                name: "reverses_transaction_id",
                schema: "leave",
                table: "toil_transactions");

            migrationBuilder.DropColumn(
                name: "source_leave_request_id",
                schema: "leave",
                table: "toil_transactions");

            migrationBuilder.DropColumn(
                name: "type",
                schema: "leave",
                table: "toil_transactions");

            migrationBuilder.DropColumn(
                name: "allow_negative_toil_balance",
                schema: "leave",
                table: "leave_types");

            migrationBuilder.DropColumn(
                name: "toil_expiry_days",
                schema: "leave",
                table: "leave_types");

            migrationBuilder.RenameColumn(
                name: "actor_employee_id",
                schema: "leave",
                table: "toil_transactions",
                newName: "awarded_by_employee_id");
        }
    }
}
