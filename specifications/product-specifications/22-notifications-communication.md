# 22. Notifications & Communication Module

## Overview

The Notifications subsystem provides a unified communication mechanism across the HR platform.

Notifications may be delivered through:

- In-app notifications
- Email notifications
- Scheduled reminders

The platform uses Postmark for email delivery and Hangfire for asynchronous processing.

---

## Business Objectives

The system shall:

- Notify users of important events
- Support in-app notifications
- Support email delivery
- Support scheduled reminders
- Track delivery status
- Maintain notification history
- Support future delivery channels

---

## Architecture

Business modules never send emails directly.

Instead:

1. Business event occurs
2. Integration event published
3. Notification created
4. Delivery queued
5. Notification dispatched

Example:

LeaveApproved
→ Notification Created
→ Email Sent
→ In-App Notification Created

---

## Notification Types

### Action Required

Examples:

- Leave approval required
- Document review required
- Task assigned

### Informational

Examples:

- Employee onboarded
- Policy updated
- Report completed

### Reminder

Examples:

- Probation due
- Document expiring
- Task overdue

---

## Delivery Channels

### In-App

Displayed in:

- Notification bell
- Notification centre

Stores:

- Title
- Message
- Action URL
- Read status

### Email

Provider:

Postmark

Used for:

- Approval requests
- Compliance reminders
- Critical alerts

---

## Notification Lifecycle

### Pending

Created but not sent.

### Sent

Successfully delivered.

### Failed

Delivery failed.

### Read

Viewed by recipient.

---

## Notification Fields

| Field | Required |
|---------|---------|
| Id | Yes |
| CompanyId | Yes |
| RecipientEmployeeId | Yes |
| Title | Yes |
| Message | Yes |
| Channel | Yes |
| CreatedAt | Yes |

---

## Templates

Templates supported for:

- Leave Requested
- Leave Approved
- Employee Created
- Candidate Hired
- Report Ready
- Document Expiring

Templates support token replacement.

Example:

{{EmployeeName}}
{{ManagerName}}
{{DueDate}}

---

## Scheduled Reminders

Generated using Hangfire.

Examples:

- 90 day probation review
- Expiring visa
- Overdue task
- Missing document

---

## Report Completion Notifications

The current report baseline returns authorised exports directly and does not require report-completion notifications.

If asynchronous stored reports are introduced for a separately approved large-report requirement, completion notifications follow this flow:

1. Report generated
2. File stored
3. Notification created
4. User receives download link

---

## Permissions

Users can:

- View own notifications
- Mark own notifications read

Administrators cannot read private notifications unless explicitly authorised.

---

## Audit Requirements

Audit:

- Notification created
- Email sent
- Delivery failed
- Notification read

---

## Reporting

Reports:

- Notifications sent
- Failed deliveries
- Read rates
- Reminder activity

---

## Acceptance Criteria

1. In-app notifications supported.
2. Email notifications supported.
3. Scheduled reminders supported.
4. Templates supported.
5. Delivery history maintained.
6. Report completion notifications are supported when an asynchronous stored-report feature is enabled.
7. Audit history maintained.
8. Company isolation enforced.
