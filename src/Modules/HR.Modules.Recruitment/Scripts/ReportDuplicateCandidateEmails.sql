-- [P1] Case-insensitive candidate email uniqueness — pre-migration duplicate report.
--
-- Migration EnforceCandidateNormalisedEmailUniqueness creates the unique index
-- ux_candidates_company_id_normalised_email on (company_id, lower(btrim(email))). If any company
-- already holds two or more candidates whose emails differ only in case or surrounding whitespace,
-- the migration stops with 'candidate_email_duplicates' and changes nothing.
--
-- Run this READ-ONLY script against the target database BEFORE deploying (and again after a failed
-- migration) to list every colliding group. It works whether or not the normalised_email column
-- exists yet, because it computes the key directly from email.
--
-- Output deliberately identifies groups by an opaque key (md5 of the normalised email) and candidate
-- ids rather than printing email addresses or names, so the result can be shared in a ticket. Look up
-- the individual candidates in the application (or by id) when resolving them.
--
-- Resolution procedure: specifications/runbooks/candidate-email-duplicates.md. Never auto-merge —
-- candidates may own applications, CV documents, interviews and hires.

WITH keyed AS (
    SELECT
        c.id,
        c.company_id,
        md5(lower(btrim(c.email))) AS email_group_key,
        c.is_active,
        c.employee_id,
        c.purged_at,
        c.created_at
    FROM recruitment.candidates c
),
colliding AS (
    SELECT company_id, email_group_key
    FROM keyed
    GROUP BY company_id, email_group_key
    HAVING count(*) > 1
)
SELECT
    k.company_id,
    k.email_group_key,
    k.id                                   AS candidate_id,
    k.created_at,
    k.is_active,
    k.employee_id,                          -- non-null = hired/linked to an employee: keep this one
    k.purged_at,
    (SELECT count(*) FROM recruitment.applications a
      WHERE a.candidate_id = k.id AND a.company_id = k.company_id)          AS application_count,
    (SELECT count(*) FROM recruitment.candidate_documents d
      WHERE d.candidate_id = k.id AND d.company_id = k.company_id)          AS document_count
FROM keyed k
JOIN colliding g
    ON g.company_id = k.company_id AND g.email_group_key = k.email_group_key
ORDER BY k.company_id, k.email_group_key, k.created_at;

-- Summary (the same numbers the migration reports):
-- SELECT count(*) AS duplicate_groups, sum(n) AS duplicate_rows, count(DISTINCT company_id) AS companies
-- FROM (SELECT company_id, count(*) AS n FROM recruitment.candidates
--       GROUP BY company_id, lower(btrim(email)) HAVING count(*) > 1) d;
