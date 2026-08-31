using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeSearchTrigramIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enable the pg_trgm extension (idempotent — safe to run if already enabled).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // GIN trigram index on the concatenated search columns. Speeds up ILIKE '%term%'
            // queries on first name, last name, work email and employee number without requiring
            // any handler code changes — the existing LIKE-based predicate uses this index
            // automatically on PostgreSQL.
            // CREATE INDEX CONCURRENTLY cannot run inside a transaction, so this statement
            // must suppress the migration transaction EF Core would otherwise wrap it in.
            migrationBuilder.Sql(@"
                CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_employees_search_trgm
                ON employees.employees
                USING GIN ((first_name || ' ' || last_name || ' ' || work_email || ' ' || employee_number) gin_trgm_ops);
            ", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS employees.ix_employees_search_trgm;");
        }
    }
}
