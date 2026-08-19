# 29. Admin Experience

## Overview

The Admin Experience provides separate company-administration, HR-administration and platform-administration capabilities. These roles must not be conflated.

The administrative experience should be powerful, secure, and easy to navigate.

Administrative functions must remain permission-controlled and fully audited.

---

## Business Objectives

The administration experience shall:

- Manage company configuration
- Manage users and employees
- Manage permissions
- Manage branding
- Manage settings
- Review audit history
- Manage reports
- Support operational governance

---

# Administrative Roles

## Company Administrator

Responsible for:

- Company configuration
- Branding
- Settings

Company Administrator alone does not grant employee management, user-role management, HR reporting, recruitment, leave, document, sickness or salary access.

The initial company creator also receives HR Administrator as an explicit separate role so that the first account can use the whole application.

---

## HR Administrator

Responsible for:

- Employee management
- Recruitment
- Leave administration
- Documents
- Compliance
- Employee user administration and role assignment where permitted

---

# Landing Behaviour

A Company Administrator without HR, Recruitment or Manager roles lands in company administration rather than an operational dashboard.

HR, Recruitment and Manager roles use their role-specific dashboards. Platform administrators use the separate `HR.Admin.Web` application.

---

# User Administration

Authorised HR or user administrators can:

- Invite users
- Disable users
- Reactivate users
- View user activity
- Assign roles

---

## User Details

Display:

- Employee
- Email
- Status
- Position
- Roles
- Last login

---

# Permission Administration

Authorised role administrators can:

- Assign roles
- Remove roles
- Add overrides
- Review effective permissions

---

## Permission Review

Display:

- Position permissions
- Overrides
- Effective permissions

---

# Company Administration

Manage:

- Company profile
- Addresses
- Contact details
- Company defaults

---

# Branding Administration

Manage:

- Logos
- Colours
- Email branding

Preview changes before publishing.

---

# Settings Administration

Manage:

- Leave settings
- Recruitment settings
- Document settings
- Notification settings
- Probation settings

---

# Report Administration

Authorised reporting users can:

- Generate reports
- Download reports
- Manage their own saved report views and favourites

Generated-file history and failed asynchronous report administration are added only if an asynchronous stored-report feature is separately approved.

---

# Audit Review

Provide access to:

- Security events
- Permission changes
- Employee changes
- Document activity

---

# Compliance Centre

Display:

- Expiring visas
- Expiring certifications
- Missing documents
- Probation reviews due

---

# Search Requirements

Administrative search should support:

- Employees
- Users
- Documents
- Candidates
- Reports

---

# Notifications

Administrators should receive:

- Compliance alerts
- Failed report notifications
- Failed integrations
- Security alerts

---

# Permissions

Administrative screens are permission-controlled.

Users should only see functionality they are authorised to access.

---

# Audit Requirements

Audit:

- User invitations
- Role changes
- Settings changes
- Branding changes
- Administrative actions

---

# UX Requirements

Administrative navigation should:

- Be grouped by category
- Minimise clicks
- Support quick search
- Support breadcrumbs

---

# Reporting

Reports:

- User activity
- Administrative changes
- Compliance status
- Security events

---

# Acceptance Criteria

1. Administrators can manage users.
2. Administrators can manage permissions.
3. Administrators can manage company settings.
4. Administrators can manage branding.
5. Administrative actions are audited.
6. Compliance information is visible.
7. Reporting is available.
8. Access is permission controlled.
9. Company Administrator alone is restricted to company profile, settings and branding.
10. Platform administration is isolated from tenant company administration.
