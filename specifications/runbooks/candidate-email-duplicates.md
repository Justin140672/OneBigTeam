# Candidate email duplicates (pre-migration check and resolution)

Applies to Recruitment migration `EnforceCandidateNormalisedEmailUniqueness`, which enforces one
candidate per company per email, compared case-insensitively and ignoring surrounding whitespace
(`recruitment.candidates.normalised_email`, unique index `ux_candidates_company_id_normalised_email`).

Companion script: `src/Modules/HR.Modules.Recruitment/Scripts/ReportDuplicateCandidateEmails.sql`.

## Why duplicates can exist

Before this change the legacy `POST /api/companies/{companyId}/candidates` checked email
case-sensitively and took no lock. That let `person@example.com` and `Person@example.com`, or two
concurrent requests, create separate candidates in the same company.

## Policy

- The migration **fails loudly and changes nothing** if duplicates exist. It raises
  `candidate_email_duplicates` with the number of colliding groups and rows. The `DETAIL` lists the
  affected `company_id` values. No email addresses or names are included.
- Duplicates are **never merged automatically**. A candidate can own applications, CV documents,
  interviews, offers and a hire link to an employee, so merging is a business decision.
- Same email in **different companies** is allowed and is not reported.

## Procedure

1. **Before deploying**, run the report script read-only against the target database. If it
   returns no rows, deploy. The migration will succeed.
2. If rows are returned (or the migration failed with `candidate_email_duplicates`), resolve each
   `(company_id, email_group_key)` group with the company's recruitment owner:
   - **Pick the survivor.** Choose the row with `employee_id` set (a hired candidate). If none has
     one, choose the active row with the most applications and documents. If that is still a tie,
     choose the oldest.
   - **Different people with a mistyped email:** correct the non-survivor's email through the
     application's candidate edit screen. Once the constraint exists, the edit screen prevents a
     new clash.
   - **Same person with no history on the non-survivor** (`application_count = 0` and
     `document_count = 0`): deactivate or remove the redundant record. Get the recruitment owner's
     approval and write down the candidate id in the change ticket.
   - **Same person with history on both:** stop and get product/HR sign-off before touching data.
     Re-pointing `recruitment.applications.candidate_id` and
     `recruitment.candidate_documents.candidate_id` is a manual, audited data fix. Watch for the
     unique `(vacancy_id, candidate_id)` constraint on applications: two applications to the same
     vacancy must be reconciled first. Do it in one transaction and record it in the change ticket.
   - Candidates already **purged** (`purged_at` set) have a unique synthetic email and never
     collide.
3. Re-run the report until it returns no rows, then re-run the deployment. The migration is safe
   to retry. It uses `IF [NOT] EXISTS` and runs in a single transaction.

## After the migration

All candidate write paths (legacy create, combined candidate + application intake, candidate
update, seed data) set `normalised_email` through the domain. Creation paths serialise on one
advisory lock (`recruitment:candidate-email:{companyId}:{normalisedEmail}`). Any remaining race is
stopped by the unique index and returned as a `409` with code `candidate_email_exists`, which
identifies the existing candidate. It is never a `500`.
