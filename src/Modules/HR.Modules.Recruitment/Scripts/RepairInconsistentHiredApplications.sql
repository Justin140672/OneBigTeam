-- Ticket 5 (P1) repair procedure.
--
-- Prior to this ticket's fix, the generic MoveApplicationStage endpoint accepted any active target
-- stage, including a company's "Hired" terminal stage, without running HireCandidate's required
-- side effects (Employee provisioning, candidate.LinkToEmployee, the CandidateHired integration
-- event). That could leave an Application sitting on a Hired-outcome stage whose Candidate was
-- never linked to an Employee — exactly the inconsistency this ticket's acceptance criteria calls
-- out for a documented repair procedure.
--
-- This script only IDENTIFIES affected rows — it deliberately does not attempt to auto-provision
-- the missing Employee (that requires cross-module orchestration: Employees-module employee
-- creation, default data, compensation, etc. — the same work HireCandidateHandler performs via
-- IEmployeeProvisioningService, which is not safe to replicate in a one-off SQL script run directly
-- against the database). Run this after deploying the MoveApplicationStage fix to find any
-- applications that need manual reconciliation (either backfilling the Employee record through
-- ordinary application flows, or moving the affected application off the Hired stage if the hire
-- was never actually completed).
--
-- Usage: run read-only first (this SELECT), review the results with HR/Recruitment ops, then
-- resolve each row individually — there is no safe one-size-fits-all automated fix.

SELECT
    a.id                AS application_id,
    a.company_id,
    a.vacancy_id,
    a.candidate_id,
    a.current_stage_id,
    rs.name             AS stage_name,
    a.updated_at        AS application_last_updated_at,
    c.employee_id        -- NULL here is the inconsistency this script surfaces.
FROM recruitment.applications a
JOIN recruitment.recruitment_stages rs
    ON rs.id = a.current_stage_id AND rs.company_id = a.company_id
JOIN recruitment.candidates c
    ON c.id = a.candidate_id AND c.company_id = a.company_id
WHERE rs.terminal_outcome = 'Hired'
  AND c.employee_id IS NULL
  AND a.withdrawn_at IS NULL
ORDER BY a.updated_at DESC;
