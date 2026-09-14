using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.DataImport.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_idempotency_keys",
                schema: "data_import",
                table: "idempotency_keys");

            migrationBuilder.AddColumn<string>(
                name: "operation_id",
                schema: "data_import",
                table: "idempotency_keys",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "data_import",
                table: "idempotency_keys",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "actor_id",
                schema: "data_import",
                table: "idempotency_keys",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                schema: "data_import",
                table: "idempotency_keys",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddPrimaryKey(
                name: "PK_idempotency_keys",
                schema: "data_import",
                table: "idempotency_keys",
                columns: new[] { "operation_id", "company_id", "actor_id", "key" });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                schema: "data_import",
                table: "idempotency_keys",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_idempotency_keys",
                schema: "data_import",
                table: "idempotency_keys");

            migrationBuilder.DropIndex(
                name: "ix_idempotency_keys_expires_at",
                schema: "data_import",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "operation_id",
                schema: "data_import",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "data_import",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "actor_id",
                schema: "data_import",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "expires_at",
                schema: "data_import",
                table: "idempotency_keys");

            migrationBuilder.AddPrimaryKey(
                name: "PK_idempotency_keys",
                schema: "data_import",
                table: "idempotency_keys",
                column: "key");
        }
    }
}
