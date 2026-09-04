# Data protection operating procedures

Owner: Crazy Cat Software Limited  
Review frequency: at least annually and after a material product, supplier or legal change  
Status: production-readiness controls; completion evidence must be retained outside the source repository

This runbook supports the Privacy Policy and Data Processing Agreement. It does not replace the
controller's responsibilities for Customer-controlled HR data.

## Data protection complaints

1. Route messages alleging misuse of personal data, inaccurate data, unwanted marketing, failure to
   honour a right, or another data-protection concern to `privacy@onebigteam.co.uk`.
2. Record the date received, complainant, affected processing, controller/processor role, owner,
   actions, evidence, outcome and closure date in the complaints register.
3. Acknowledge within 30 calendar days. Verify identity only where reasonably necessary and do not
   collect excessive identification evidence.
4. Investigate without undue delay. Preserve relevant records and involve security where a breach
   may have occurred.
5. Where Customer-controlled data is involved, forward the complaint securely to the Customer and
   assist it under the DPA. Continue investigating any allegation about Crazy Cat Software's own
   processing or processor conduct.
6. Give the complainant a clear outcome, reasons, remedial action and information about the right to
   complain to the ICO. Record any follow-up.

## Rights requests

1. Log the request and identify whether Crazy Cat Software or a Customer is controller.
2. For Customer-controlled data, promptly forward the request to an authorised Customer contact and
   do not respond substantively unless instructed or legally required.
3. Search relevant account, billing, support, communications, security and supplier systems when
   Crazy Cat Software is controller. Apply the current statutory time calculation and document any
   identity check, clarification, extension or exemption.
4. Use secure delivery for exports. Record what was searched, decisions, redactions, approval and
   completion. Delete any temporarily staged assisted export files within seven days.

## Customer cancellation, return and deletion

1. Confirm the effective cancellation date and the end of the 30-day recovery period.
2. Tell an authorised Customer administrator to obtain available self-service exports and offer
   reasonable assistance for other data before recovery ends.
3. At recovery expiry, disable ordinary tenant access and open a deletion record. Do not mark it
   complete merely because access has been disabled.
4. Inventory and delete tenant records across every application module, Supabase private objects,
   authentication profiles, queued/staged imports, generated exports, support attachments and other
   Customer-controlled stores. Record per-store completion and failures. This explicitly includes
   special-category equality-monitoring data in `employees.employee_equality_data`: it has an
   `ON DELETE CASCADE` foreign key to `employees.employees`, so deleting the employee rows (or
   dropping the `employees` schema) removes it automatically — verify the table is empty for the
   tenant as part of per-store sign-off. The stored answer columns are ciphertext only; the
   encryption keys are held outside the database and are not part of any backup.
5. Preserve only information covered by a documented legal-retention exception. Separate it from
   ordinary use, restrict access and record the lawful reason and review/deletion date.
6. Record the applicable provider backup expiry. If disaster recovery restores an earlier copy,
   reapply all still-valid deletion records before returning the service to ordinary use.
7. A second authorised person verifies completion. Notify the Customer when deletion is complete.

The current administrative `ExecuteCustomerDeletion` action disables access and records lifecycle
state; it is not evidence that step 4 has completed. Production operations must not describe a
Customer as deleted until every required store has been checked.

## Personal data breaches

1. Route suspected incidents immediately to `security@onebigteam.co.uk`; record detection time and
   the time Crazy Cat Software became aware of a personal data breach.
2. Contain the incident without destroying evidence. Identify affected Customers, people, data,
   systems, dates, likely consequences and mitigations.
3. Notify each affected Customer without undue delay and, where practicable, within 48 hours. Supply
   information in stages if necessary and maintain a decision/contact log.
4. For Crazy Cat Software's controller processing, assess risk to people and document the decision
   whether to notify the ICO within 72 hours and affected people without undue delay.
5. Record every personal data breach, including those not notified, the reasoning and corrective
   measures. Complete a lessons-learned review.

## Records and assurance

Maintain and periodically review:

- an Article 30 controller ROPA and processor ROPA;
- the ICO fee/registration decision and renewal evidence;
- supplier Article 28 terms, role assessments and current subprocessor list;
- UK transfer mechanism, transfer-risk assessment and supplementary measures per restricted transfer;
- retention schedule and deletion evidence;
- DPIAs, including one before enabling equality/diversity or other new high-risk processing;
- legitimate-interest assessments for controller processing relying on legitimate interests;
- staff confidentiality terms, training and access reviews;
- incident, breach, complaint and rights-request registers;
- backup configuration, expiry and restoration-test evidence; and
- vulnerability, patching and security-test evidence.

## Production launch gates

Do not process live Customer personal data until all applicable gates have recorded approval:

- production tenant isolation and role checks pass;
- supplier contracts, transfer safeguards and locations are verified;
- database backup retention and restoration are tested;
- any advertised independent file backup is implemented and tested;
- full customer deletion has a tested per-store procedure and accountable operator;
- Customer data return can be completed using exports plus the assisted process;
- equality/diversity collection remains disabled until its DPIA, field-level protection, access
  restrictions and audit tests are approved;
- complaint, rights-request and breach channels are monitored by named people with cover arrangements; and
- sensitive-data encryption keys exist for the target environment (distinct per environment), are
  held in the vault plus a secondary copy, Railway service variables are set, and `/health/ready`
  detail reports the `sensitive-data-encryption` check as Healthy — see
  `docs/runbooks/encryption-key-management.md`.

## Encryption key management

Sensitive and special-category fields (currently equality-monitoring answers) are encrypted at the
application layer before persistence; the AES-256 keys live only in environment/secret configuration
and never in the database, a backup or source control. The API host fails fast at startup in every
non-Development environment if the key configuration is absent or invalid — it will not serve traffic
in that state. The full procedure (initial production key creation, Railway secret configuration,
staging configuration, backup implications, key recovery requirements, and the rotation procedure) is
in `docs/runbooks/encryption-key-management.md`.
