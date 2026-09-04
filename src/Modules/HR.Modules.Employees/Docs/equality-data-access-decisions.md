# Equality & Diversity data — access decisions

## Scope

`EmployeeEqualityData` holds voluntary equality-monitoring answers. These are special-category
personal data, encrypted at rest by the application layer, and are treated as strictly
self-service.

## Individual access (Ticket 5)

* **Only the subject employee** can view or edit their own answers. Enforced by:
  * the self-service endpoints (`GetMyEqualityData`, `SaveMyEqualityData`, `DeleteMyEqualityData`)
    being gated on `role:employee` **and** rejecting any request whose target `employeeId` is not
    the authenticated user (`403`);
  * no equality fields appearing on any general employee response DTO, list, directory/search
    result or the `GetEmployee` detail contract (asserted by
    `tests/HR.Architecture.Tests/EmployeeResponseDtoEqualityFieldExposureTests.cs`);
  * audit events carrying presence flags only, never answer values.
* Company Admin / Employee Manager / Recruitment / general HR roles have **no** UI or API route to
  an individual's answers. The self-service policy cannot be satisfied for another employee by any
  role because of the per-request subject check.

## Exceptional administrative access — deliberately NOT implemented

There is currently **no** endpoint, service, job or UI that lets any role retrieve a *named*
individual's equality answers. This is intentional:

* There is no current product requirement for a data-protection/compliance officer to read an
  individual's equality answers. The lawful basis for collection is equality monitoring, which is
  served entirely by the anonymous aggregate report (Ticket 6).
* The codebase has no existing fine-grained "compliance officer may read one person's sensitive
  record" pattern to slot into. `compliance.view` (ADM-02) gates aggregate compliance dashboards,
  not individual sensitive records, and the former governance permissions were retired.
* Adding a decrypting "read one person's answers" path now would be a standing re-identification
  risk with no consumer.

## Lifecycle, retention and deletion (Ticket 8)

`EmployeeEqualityData` follows the employee lifecycle and the existing company/employee
retention rules. It is **not** an independently retained record.

* **Association.** Every row carries `company_id` and `employee_id`, has a unique
  `(company_id, employee_id)` index (max one record per employee), and — as of migration
  `AddEmployeeEqualityDataEmployeeForeignKey` — a real foreign key to `employees.employees.id`
  with `ON DELETE CASCADE`. A row therefore cannot exist without its employee and cannot outlive a
  physical delete of that employee.
* **Cross-company access** is blocked before any handler runs by
  `TenantRouteAuthorizationMiddleware`: every equality route is under
  `/api/companies/{companyId}/…` and the middleware 403s when `{companyId}` does not match the
  caller's DB-resolved tenant. The self-service endpoints additionally reject any `employeeId`
  that is not the authenticated user, and every handler filters on `company_id` + `employee_id`.
  The aggregate report handler filters strictly on `request.CompanyId`.
* **When an employee leaves.** Nothing is deleted. The employee row is soft-deleted
  (`Status = FormerEmployee`) and the equality record is retained while that row exists — former
  employees remain in scope for equality monitoring and the customer is still the controller.
  The record is destroyed only when:
  1. the employee withdraws it themselves (`DeleteMyEqualityData`); or
  2. the employee row is **physically** deleted — the manual per-store customer-deletion
     procedure in `docs/compliance/data-protection-operations.md` step 4, or full-tenant deletion
     which drops the entire `employees` schema. The cascade FK makes this automatic; no separate
     equality-specific purge step or job is required, which is what prevents an orphan.
* **Permanent employee/company deletion.** There is no automated per-employee hard-delete in the
  product today (see `docs/compliance/data-retention-inventory.md` §1). When the operator runs the
  per-store deletion, deleting the `employees.employees` rows cascades to
  `employee_equality_data`; dropping the `employees` schema removes it outright. Either way the
  ciphertext is gone with the row.
* **Encryption keys and backups.** Answer columns are AES-256-GCM ciphertext
  (`AesGcmSensitiveDataProtector`). Keys live only in environment/secret configuration
  (`Infrastructure:SensitiveDataProtection:Keys`) — never in `appsettings.json` and never in any
  application table — so a database backup contains only ciphertext and cannot be decrypted from
  its own contents. A restored backup is subject to the deletion-obligation re-application step in
  `docs/runbooks/backup-and-disaster-recovery.md` §5.5 like any other tenant data.

### Hook point if this changes

If a genuine, documented need arises, implement it as a **separate** vertical slice
(`Features/GetEmployeeEqualityDataForCompliance/`) that:

1. is gated on a **new** fine-grained permission (e.g. `employee:read-equality-data`) granted to a
   dedicated data-protection role only — never inherited from `employee:read` or any general HR
   role;
2. requires an explicit `reason` on the request;
3. emits a distinct audit event (`employee.equality_data.accessed`) recording actor, subject and
   reason — unlike the existing self-service events this one records that a *third party* read the
   data;
4. still never returns the data on any shared/general employee DTO.
