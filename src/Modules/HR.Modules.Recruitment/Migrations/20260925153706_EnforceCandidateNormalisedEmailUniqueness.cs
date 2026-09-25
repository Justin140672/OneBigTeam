using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Recruitment.Migrations
{
    /// <summary>
    /// [P1] Case-insensitive candidate email uniqueness: adds the persisted canonical email
    /// (<c>normalised_email</c> = trimmed + lower-cased, see Domain/CandidateEmail.cs), backfills it,
    /// and replaces the non-unique (company_id, email) index with the unique
    /// <c>ux_candidates_company_id_normalised_email</c>.
    ///
    /// Duplicate policy — fail loudly, never merge: existing rows were written by a case-sensitive
    /// legacy check, so a company may already hold two candidates whose emails differ only in case or
    /// surrounding whitespace. Before the unique index is created the migration counts such groups and,
    /// if any exist, RAISEs with the number of colliding groups/rows and the affected company ids (no
    /// email addresses or names). EF runs each migration in a transaction, so the column add/backfill
    /// rolls back and the schema is left exactly as before. Candidates may own applications, documents
    /// and hires, so resolution is a manual, audited decision — see
    /// specifications/runbooks/candidate-email-duplicates.md and the pre-migration report
    /// src/Modules/HR.Modules.Recruitment/Scripts/ReportDuplicateCandidateEmails.sql.
    ///
    /// The backfill expression lower(btrim(email)) matches CandidateEmail.Normalise for the stored
    /// data: every stored email was already trimmed by the domain, and lower() and ToLowerInvariant()
    /// agree for ASCII email addresses.
    ///
    /// Supabase Data API: no table, view, sequence or function is created — one column and one index
    /// on the existing server-only (direct PostgreSQL) recruitment.candidates table. No grants are
    /// added for anon, authenticated or service_role.
    /// </summary>
    public partial class EnforceCandidateNormalisedEmailUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE recruitment.candidates
                    ADD COLUMN IF NOT EXISTS normalised_email character varying(256) NULL;

                UPDATE recruitment.candidates
                SET normalised_email = lower(btrim(email))
                WHERE normalised_email IS DISTINCT FROM lower(btrim(email));
                """);

            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    duplicate_groups integer;
                    duplicate_rows integer;
                    affected_companies text;
                BEGIN
                    SELECT count(*), coalesce(sum(row_count), 0)
                    INTO duplicate_groups, duplicate_rows
                    FROM (
                        SELECT count(*) AS row_count
                        FROM recruitment.candidates
                        GROUP BY company_id, normalised_email
                        HAVING count(*) > 1
                    ) AS duplicates;

                    IF duplicate_groups > 0 THEN
                        SELECT string_agg(format('%s (%s group(s))', company_id, groups), ', ' ORDER BY company_id)
                        INTO affected_companies
                        FROM (
                            SELECT company_id, count(*) AS groups
                            FROM (
                                SELECT company_id
                                FROM recruitment.candidates
                                GROUP BY company_id, normalised_email
                                HAVING count(*) > 1
                            ) AS g
                            GROUP BY company_id
                        ) AS per_company;

                        RAISE EXCEPTION 'candidate_email_duplicates: cannot create ux_candidates_company_id_normalised_email: % duplicate (company_id, normalised email) group(s) covering % candidate row(s) in recruitment.candidates.', duplicate_groups, duplicate_rows
                            USING DETAIL = 'Affected company_id values: ' || affected_companies,
                                  HINT = 'No data was changed. Run src/Modules/HR.Modules.Recruitment/Scripts/ReportDuplicateCandidateEmails.sql and resolve each group per specifications/runbooks/candidate-email-duplicates.md, then re-run the migration.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE recruitment.candidates
                    ALTER COLUMN normalised_email SET NOT NULL;

                DROP INDEX IF EXISTS recruitment."IX_candidates_company_id_email";

                CREATE UNIQUE INDEX IF NOT EXISTS ux_candidates_company_id_normalised_email
                    ON recruitment.candidates (company_id, normalised_email);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS recruitment.ux_candidates_company_id_normalised_email;

                CREATE INDEX IF NOT EXISTS "IX_candidates_company_id_email"
                    ON recruitment.candidates (company_id, email);

                ALTER TABLE recruitment.candidates
                    DROP COLUMN IF EXISTS normalised_email;
                """);
        }
    }
}
