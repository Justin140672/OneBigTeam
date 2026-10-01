# Data presentation policies

## Headcount definition (Option A)

- Total Headcount counts every employee record whose status is not Draft.
- Every counted employee falls into exactly one category, so
  Total Headcount = Active + Future Starters + Leavers + Suspended or Serving Notice.
- Precedence when classifying: Leaver (Former Employee, or leaving date on or before today), then Future Starter
  (start date after today), then Active (status Active), then Suspended or Serving Notice.
- Draft records are excluded from the KPI cards, the report table, report exports, status filters, saved views and the
  dashboard charts (department, employment type, gender), which show the Active category only.
- Implementation: `HeadcountRules` in HR.Modules.Employees is the single source of the rules.

## Employee display name

- Display name is the preferred name when it exists and differs from the legal first name, otherwise the legal first
  name, followed by the legal last name. Implementation: `HR.SharedKernel.PersonName.Display`.
- The display name is used in the directory, profile header, search results, reports and exports, tasks,
  notifications, document and request titles, manager and reporting-line labels and audit history labels.
- The legal name is shown explicitly where it matters: the employee profile header shows "Legal name: ..." when the
  preferred name differs, personal-details change requests use `PersonName.DisplayWithLegal`, and data exports carry the
  legal first name, last name and preferred name as separate columns.
- Search matches preferred name, legal first name, last name, and both full-name combinations.

## Employment types

- Employment types are always filtered and grouped by `EmploymentTypeId`; the label is read from the configured record.
- Dashboard charts and the headcount report share `EmploymentTypeGrouping`, so the same population produces the same
  groups and labels.
- Non-canonical values are flagged rather than hidden: inactive types are labelled "(inactive)", types created
  automatically by a data import and not yet reviewed (`requires_review`) are labelled "(unreviewed import)", and ids that
  no longer resolve are labelled "Unknown (legacy value)". Opening and saving an imported type in Employment Types
  confirms it and clears the flag.
