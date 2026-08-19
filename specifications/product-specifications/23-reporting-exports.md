# 23. Reporting and Exports

## Current status

The Reporting module is implemented. It provides a formal report catalogue, permission-scoped report views, filtering and grouping, saved views, favourites and supported server export endpoints.

Reporting is separate from local Syncfusion grid export. A feature screen may export its visible grid without becoming a formal report.

## Implemented report catalogue

The current catalogue includes:

- Employee directory
- Employee starters
- Employee leavers
- Leave summary
- Leave calendar
- Sickness
- Probation
- Onboarding progress
- Offboarding progress
- Recruitment pipeline
- Vacancy performance
- Document compliance
- Company document acknowledgement
- Asset assignments
- Workload actions

## Data ownership

The Reporting module owns report definitions, saved views, favourites and export orchestration. It does not own the underlying HR records.

Source modules expose purpose-specific read contracts. Reporting must not query another module's DbContext or tables directly.

## Report behaviour

- Reports execute against current tenant-scoped data.
- Filters and grouping are validated by the API.
- Saved views belong to the authenticated user and company.
- A user may mark a saved view as their default for a report.
- A user may favourite reports available to their permissions.
- Export endpoints apply the same authorization and filters as the corresponding report view.
- Report results and exports must remain bounded and should use pagination or a justified export limit.

## Authorization

Every report requires server-side authorization.

- HR reports require HR reporting access.
- Recruitment reports require Recruitment reporting access.
- Manager reports are limited to the manager's complete reporting hierarchy where the report supports manager access.
- Company Administrator alone does not grant access to employee or HR reports.
- Sensitive salary or compensation reporting follows the compensation access rules in `00-current-product-decisions.md`.
- Every request remains subject to authenticated-tenant verification.

The report catalogue must omit reports the caller cannot use, but catalogue filtering does not replace endpoint authorization.

## Exports

Supported exports are generated on demand and returned through authorised endpoints. CSV is the baseline server export format; other formats may be supported by the shared report exporter where implemented.

Export requirements:

- Use a safe filename.
- Escape spreadsheet-formula prefixes in user-controlled values.
- Apply tenant and resource authorization before querying and again before returning a file where the operation is deferred.
- Do not include fields absent from the corresponding authorised report.
- Audit sensitive exports where required by the report classification.

## Asynchronous and stored reports

Asynchronous report generation, stored generated-report files, report history, scheduled reports and report subscriptions are not general MVP requirements and are not part of the current baseline.

If a demonstrated large-report requirement cannot meet the normal response target, it may introduce a bounded Hangfire job and private Supabase Storage object. That work requires an explicit feature decision covering retention, idempotency, authorization at download time, failure visibility and cleanup.

## Payroll boundary

Reporting may export employee or compensation information for authorised operational use. It does not calculate payroll, tax, deductions or statutory payroll submissions.

## Performance

- Small reports should complete within 10 seconds under the supported tenant-size assumptions.
- Queries must avoid unbounded materialisation and N+1 access patterns.
- Filters used by common reports should be supported by relevant source-module indexes.
- Large-report behaviour must be measured before asynchronous infrastructure is introduced.

## Acceptance criteria

1. The formal report catalogue is available to authorised users.
2. Reports are filtered by authenticated tenant and resource scope.
3. The implemented catalogue covers the report types listed above.
4. Saved views, defaults and favourites are scoped to the current user and company.
5. Export endpoints enforce the same access and filters as report views.
6. Sensitive reports and exports follow their owning data permissions.
7. Company Administrator alone does not grant HR reporting access.
8. Reporting uses module-owned read contracts and does not query another module's schema directly.
9. Grid export remains available independently on screens that support it.
10. Asynchronous stored reports are added only for a separately approved requirement.
