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

## Dashboard workload: actionable and visibility-only items

The HR and manager dashboards separate two kinds of outstanding work. Classification is computed on the server by
`DashboardSummaryComposer` from the signed-in user, the item's responsible party and its source record. It is never
inferred from task type or priority, and nothing a client sends can change it.

| Classification | Meaning | Where it appears |
|---|---|---|
| `CanAct` | The user is authorised and expected to complete it: the underlying task is assigned to them, or belongs to an HR-owned queue (unassigned onboarding, offboarding), and a working destination exists | "Needs your action" |
| `VisibilityOnly` | Outstanding, but someone else is responsible (a manager's leave approval, an employee's evidence upload). HR can monitor it but not complete it | "Waiting on others" |
| `Unavailable` | The source record is missing or inconsistent, or an actionable item has no destination | Neither queue. Reported as an explicit exception (HR dashboard) with an investigation link where one exists, and recorded as a warning log and the `dashboard.workload.unavailable` counter |

- Items whose source is completed, cancelled or deleted are not returned by providers, so they never reach either queue.
- A waiting item never carries a task id or deep link. It shows the responsible person or role, status, due or overdue
  state, why HR sees it and, where one exists, a read-only monitoring link (the employee profile).
- Counts: `TotalActionableCount` is the number of `CanAct` items and drives the "Needs your action" badge and the manager
  dashboard activity summary. `TotalWaitingOnOthersCount` is the number of `VisibilityOnly` items and drives the
  "Waiting on others" badge. They are never summed into one headline figure.
- The notification bell counts unread notifications (events). It is a separate feed, not a second count of this workload.
  Task-backed notifications deep-link to the same task that appears in "Needs your action".
- Ownership rules for HR scope are in `WorkloadOwnership.ForHrViewer`: assigned to the viewer is `CanAct`; unassigned is
  `CanAct` only for HR-owned processes; assigned to anyone else is `VisibilityOnly`. A user who holds both the HR and
  manager roles acts on items assigned to them and monitors the rest.
- Priority and overdue styling are independent of ownership.
