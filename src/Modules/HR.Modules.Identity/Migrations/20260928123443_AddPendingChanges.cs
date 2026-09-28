using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: Ticket7_RemoveRedundantPermissions is the authoritative owner of the candidate.view
            // permission removal. This migration previously duplicated that operation, which caused
            // rollback failures when both migrations were rolled back (the newer migration would restore
            // the records first, then the older one would try to restore them again, causing constraint
            // violations). The duplicate operations have been removed; Ticket7 handles the removal.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op. Ticket7_RemoveRedundantPermissions handles restoration.
        }
    }
}
