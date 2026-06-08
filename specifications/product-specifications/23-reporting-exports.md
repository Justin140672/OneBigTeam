# 23. Reporting & Exports Module

## Overview

The Reporting & Exports capability provides operational reports, compliance reports, ad-hoc grid exports, and generated files across the HR platform.

The system supports two export paths:

1. Lightweight client-side exports from Syncfusion grids.
2. Server-side generated reports for formal, sensitive, or long-running reporting.

Server-side reports are generated asynchronously using Hangfire, stored in Supabase Storage, and surfaced to the user through in-app notifications.

---

## Business Objectives

The reporting capability shall:

- Allow users to export grid data where permitted.
- Provide formal HR reports.
- Support asynchronous report generation.
- Store generated reports securely.
- Notify users when reports are ready.
- Enforce company and permission boundaries.
- Audit sensitive report generation and downloads.
- Support CSV and Excel as primary formats.
- Support PDF only where business-appropriate.

---

## Reporting Types

### 1. Grid Export

Used for simple ad-hoc exports from visible grid data.

Examples:

- Employee list
- Leave requests
- Task list
- Recruitment candidate list
- Document list

Implemented using Syncfusion grid export functionality.

Grid exports should reflect:

- Current filters
- Current sorting
- Current grouping
- Current visible columns

Grid exports must not bypass permissions.

---

### 2. Formal Reports

Used for reports that are:

- Sensitive
- Large
- Compliance-related
- Repeatable
- Long-running
- Multi-table
- Stored for later download

Examples:

- Monthly absence report
- Headcount report
- Probation due report
- Missing documents report
- Expiring documents report
- Recruitment pipeline report
- Audit activity export
- Payroll-ready employee export

Formal reports are generated server-side.

---

## Report Generation Flow

1. User requests report.
2. System validates permissions.
3. Report request record is created.
4. Hangfire job is queued.
5. Report is generated server-side.
6. File is written to Supabase Storage.
7. Report metadata is updated.
8. User receives system notification.
9. User downloads report through authorised endpoint.
10. Download is audited.

---

## Report Request Entity

| Field | Required | Notes |
|---|---|---|
| Id | Yes | Unique report request identifier |
| CompanyId | Yes | Tenant boundary |
| RequestedByEmployeeId | Yes | Employee requesting report |
| ReportType | Yes | System report type |
| ParametersJson | Optional | Report filters/options |
| Status | Yes | Pending, Processing, Completed, Failed |
| OutputFormat | Yes | CSV, XLSX, PDF |
| StoragePath | No | Set after generation |
| CreatedAt | Yes | Request timestamp |
| StartedAt | No | Processing timestamp |
| CompletedAt | No | Completion timestamp |
| FailedAt | No | Failure timestamp |
| FailureReason | No | Redacted failure detail |
| ExpiresAt | Optional | Optional expiry/retention date |

---

## Report Statuses

### Pending

Report has been requested but not started.

### Processing

Background job is generating the report.

### Completed

Report file is available for download.

### Failed

Report generation failed.

Failures should be visible to the requester and administrators.

### Expired

Report is no longer available.

---

## Supported Formats

### CSV

Use for:

- Raw exports
- Payroll-ready exports
- Integration-friendly data

CSV should be UTF-8 encoded.

### Excel XLSX

Use for:

- Formal HR reports
- Multi-sheet reports
- Styled operational reports

Recommended for most HR reporting.

### PDF

Use only for:

- Formal letters
- Policy packs
- Compliance documents
- Printable outputs

Avoid PDF for large analytical reports.

---

## Storage

Generated reports are stored in Supabase Storage.

Recommended bucket:

```text
generated-reports
```

Recommended path format:

```text
{companyId}/reports/{reportType}/{reportId}.{extension}
```

Reports should be private.

Downloads must use signed URLs generated after permission checks.

---

## Report Notification Behaviour

When report generation completes, the system creates an in-app notification:

```text
Your report is ready for download.
```

The notification should include:

- Report name
- Generated date
- Download action
- Expiry date if applicable

Optional email may be sent for long-running or compliance-critical reports.

---

## Permissions

Report access must be permission-controlled.

Examples:

### Employee

Can export own visible data only.

### Manager

Can generate reports for direct and indirect reports where permitted.

### HR Admin

Can generate company-wide HR reports.

### Company Admin

Can generate administrative/company reports.

### Finance

Can generate payroll-related reports if assigned the correct role.

---

## Sensitive Reports

Sensitive reports include:

- Salary reports
- Compensation history
- Sickness reports
- Audit exports
- Document compliance exports
- Termination reports

Sensitive reports require explicit permissions.

Sensitive report generation must be audited.

---

## Audit Requirements

Audit events must be created for:

- Report requested
- Report generated
- Report failed
- Report downloaded
- Report expired
- Sensitive report generated

Audit metadata should include:

- Report type
- Requesting employee
- Filters used where safe
- Output format
- Whether the report was sensitive

Sensitive filter values should be redacted.

---

## Validation Rules

- Report type must be valid.
- Output format must be supported.
- User must have permission for requested scope.
- Date ranges must be valid.
- Report parameters must be validated before job creation.
- Large reports must use background generation.
- Report downloads must validate access at download time.

---

## UX Requirements

### Report Request Screen

Users should be able to:

- Select report type
- Select format
- Enter report parameters
- Submit report request

### Report History

Users should see:

- Recently generated reports
- Status
- Created date
- Expiry
- Download action

### Admin Report View

HR/Admin users may see wider report history depending on permissions.

---

## Acceptance Criteria

1. Users can export permitted grid data.
2. Formal reports are generated server-side.
3. Long-running reports run in Hangfire.
4. Generated reports are stored in Supabase Storage.
5. Users receive notification when report is ready.
6. Report downloads are permission-checked.
7. Sensitive reports are audited.
8. Report failures are visible.
9. CSV and XLSX are supported.
10. PDF is supported only for appropriate report types.
