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
