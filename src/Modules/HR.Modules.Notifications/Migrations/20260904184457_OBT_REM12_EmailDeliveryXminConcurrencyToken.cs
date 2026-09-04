using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class OBT_REM12_EmailDeliveryXminConcurrencyToken : Migration
    {
        /// <summary>
        /// OBT-REM-12: intentionally a no-op. "xmin" is PostgreSQL's built-in system column, present
        /// on every table already — it is not a real user column to add. This migration exists only
        /// to bring the EF Core migration snapshot in line with the model change in
        /// EmailDeliveryConfiguration (a shadow property mapping the existing xmin system column as
        /// an optimistic concurrency token), which otherwise trips EF Core's
        /// PendingModelChangesWarning at startup. Running `ADD COLUMN xmin` against PostgreSQL would
        /// fail outright ("column name xmin conflicts with a system column name").
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
