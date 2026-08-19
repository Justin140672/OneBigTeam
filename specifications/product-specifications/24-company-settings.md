# 24. Company Settings Module

## Overview

The Company Settings module stores tenant-specific configuration that controls platform behaviour.

This module is separate from:

- Company Profile
- Company Branding

Settings define how the platform behaves for a company.

---

## Business Objectives

The module shall:

- Store company-specific configuration
- Support HR policy configuration
- Support leave configuration
- Support working pattern configuration
- Support notification configuration
- Support future extensibility
- Support auditing of configuration changes

---

## Architecture Principles

Settings are:

- Company-scoped
- Audited
- Version-safe
- Extensible

Settings are not stored as generic key/value pairs for core business behaviour.

Critical settings should be strongly typed.

---

# Company Settings Categories

## General Settings

### Company Time Zone

Used for:

- Notifications
- Reports
- Scheduling

### Default Locale

Used for:

- Formatting
- Date presentation
- Regional behaviour

### Working Week

Examples:

- Monday-Friday
- Sunday-Thursday

---

## Leave Settings

### Leave Year Start

Examples:

- January 1
- April 1

### Default Annual Allowance

Default holiday entitlement.

### Carry Forward Rules

Options:

- Not allowed
- Limited carry forward
- Unlimited carry forward

### Approval Required

Determines whether leave requests require approval.

---

## Probation Settings

### Default Probation Length

Examples:

- 3 months
- 6 months
- 12 months

### Reminder Schedule

Examples:

- 30 days before
- 14 days before
- 7 days before

---

## Recruitment Settings

### Vacancy Approval Required

Boolean.

### Offer Approval Required

Boolean.

### Candidate Retention Period

Retention period after recruitment completion.

---

## Document Settings

### Mandatory Documents

Examples:

- Contract
- Passport
- Right To Work

### Expiry Reminder Schedule

Examples:

- 90 days
- 30 days
- 7 days

---

## Notification Settings

### Email Enabled

Enable/disable email notifications.

### Reminder Enabled

Enable/disable reminder notifications.

### Report Completion Notifications

Reserved for a separately approved asynchronous stored-report feature. It is not required by the current on-demand reporting baseline.

---

## Branding Integration

Settings reference branding configuration but do not store branding assets.

Branding remains within:

- Company Branding

---

# Data Model

## Company Settings

| Field | Required |
|---------|---------|
| CompanyId | Yes |
| TimeZone | Yes |
| Locale | Yes |
| WorkingWeek | Yes |
| LeaveYearStart | Yes |
| DefaultHolidayAllowance | Yes |
| ProbationMonths | Yes |

Additional settings may be added through versioned schema changes.

---

# Permissions

## Employee

Read-only access where appropriate.

Cannot modify settings.

## Manager

Limited visibility.

Cannot modify settings.

## HR Admin

Can manage HR-related settings.

## Company Admin

Full settings management.

---

# Validation Rules

- Leave year start must be valid.
- Holiday allowance cannot be negative.
- Probation length must be greater than zero.
- Time zone must be supported.
- Locale must be supported.

---

# Audit Requirements

Audit:

- Setting created
- Setting updated
- Setting reset

Audit entries should capture:

- Previous value
- New value
- User making change

Sensitive values may be redacted.

---

# Reporting

Reports:

- Settings changes
- Configuration history
- Compliance configuration review

---

# UI Requirements

## Settings Dashboard

Grouped by category:

- General
- Leave
- Recruitment
- Documents
- Notifications

## Settings History

Display:

- Who changed setting
- When change occurred
- Previous value
- New value

---

# Acceptance Criteria

1. Settings are company-scoped.
2. Settings support validation.
3. Settings support auditing.
4. Settings are grouped by category.
5. HR Admins can manage HR settings.
6. Company Admins can manage all settings.
7. Configuration history is available.
8. Changes are reflected immediately where appropriate.
