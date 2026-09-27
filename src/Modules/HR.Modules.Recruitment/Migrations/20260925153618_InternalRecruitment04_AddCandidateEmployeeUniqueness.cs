using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <summary>
    /// Internal recruitment Ticket 4: at most one Candidate per (company, employee).
    ///
    /// Existing data: candidates.employee_id has only ever been set by HireCandidate
    /// (Candidate.LinkToEmployee refuses to relink, and each hire provisions a distinct employee keyed
    /// by its application), so no two candidates should share an employee. Rather than silently
    /// unlinking anything, the migration stops with an actionable error if that assumption is ever
    /// false — the transaction rolls back and nothing is changed. Diagnose with:
    ///   SELECT company_id, employee_id, array_agg(id ORDER BY created_at) AS candidate_ids
    ///   FROM recruitment.candidates WHERE employee_id IS NOT NULL
    ///   GROUP BY company_id, employee_id HAVING count(*) > 1;
    ///
    /// Hand-written with IF [NOT] EXISTS so it is safe to replay and cannot collide with another
    /// migration that happens to include the same index (the Recruitment model was being changed by a
    /// parallel piece of work when this was authored).
    ///
    /// Supabase Data API: an index only — no table, view, sequence or function is created, and no
    /// grants are added. The candidates table remains server-only (direct PostgreSQL).
    /// </summary>
    public partial class InternalRecruitment04_AddCandidateEmployeeUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    duplicate_groups integer;
                BEGIN
                    SELECT count(*) INTO duplicate_groups
                    FROM (
                        SELECT 1
                        FROM recruitment.candidates
                        WHERE employee_id IS NOT NULL
                        GROUP BY company_id, employee_id
                        HAVING count(*) > 1
                    ) AS duplicates;

                    IF duplicate_groups > 0 THEN
                        RAISE EXCEPTION 'Cannot create ix_candidates_company_id_employee_id: % (company_id, employee_id) group(s) in recruitment.candidates are linked to more than one candidate. Reconcile them manually (see migration InternalRecruitment04_AddCandidateEmployeeUniqueness) and re-run.', duplicate_groups;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS ix_candidates_company_id_employee_id
                    ON recruitment.candidates (company_id, employee_id)
                    WHERE employee_id IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS recruitment.ix_candidates_company_id_employee_id;");
        }
    }
}
