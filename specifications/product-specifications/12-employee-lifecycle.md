# 12. Employee Lifecycle

## Overview

The Employee Lifecycle capability manages an employee from creation through employment changes, position changes, manager changes, termination and historical record keeping.

This is one of the core areas of the HR platform.

---

## Business Objectives

The system shall:

- Create employee records
- Maintain employment history
- Support position assignment
- Support manager assignment
- Support department assignment
- Track lifecycle changes
- Support termination workflows
- Preserve historical data
- Audit all major lifecycle events

---

## Employee States

### Draft

Employee record has been created but is incomplete.

### Active

Employee is currently employed.

### On Leave

Employee is active but temporarily absent.

### Suspended

Employee remains employed but access/workflow participation may be restricted.

### Terminated

Employee employment has ended.

Terminated employees are not hard deleted.

---

## Employee Core Fields

| Field | Required | Notes |
|---|---|---|
| First Name | Yes | Legal or preferred first name |
| Last Name | Yes | Legal or preferred surname |
| Work Email | Yes | Unique within company |
| Personal Email | Optional | Used where appropriate |
| Start Date | Yes | Employment start date |
| Position Profile | Yes | Drives default roles/permissions |
| Department | Optional | Used for reporting and org structure |
| Manager | Optional | Used for hierarchy and approvals |
| Employment Status | Yes | Current lifecycle status |

---

## Employment History

The system must preserve employment history including:

- Start dates
- End dates
- Position changes
- Department changes
- Manager changes
- Employment type changes
- Termination details

---

## Manager Assignment

Managers are assigned explicitly using a manager employee reference.

Manager hierarchy is used for:

- Leave approval
- Team visibility
- Task routing
- Dashboard summaries
- Permission scope evaluation

---

## Position Assignment

Employees are assigned to a Position Profile.

Position Profile determines:

- Default role(s)
- Permission defaults
- Managerial status
- Department alignment
- Reporting expectations

---

## Termination Workflow

The authoritative leaving and offboarding contract is `34-leaving-offboarding.md` (`SPEC-OFF-01`). Its date definitions, routes, state transitions, finalisation, cancellation and cross-module rules supersede any conflicting interpretation in this section.

Termination should support:

- Termination date
- Termination reason
- Exit tasks
- Document retention review
- Asset recovery
- Account access removal
- Final audit entry

---

## Automation

Employee lifecycle events may trigger:

- Onboarding tasks
- Probation tasks
- Document requests
- Notifications
- Permission recalculation
- Audit events

---

## Audit Events

Audit:

- Employee created
- Employee updated
- Manager changed
- Position changed
- Department changed
- Employment terminated
- Employment reactivated

---

## Permissions

### Employee

Can view own profile.

### Manager

Can view direct and indirect reports where permitted.

### HR Admin

Can manage employee records.

### Company Admin

Company Administrator alone cannot view or manage detailed employee records. The role is limited to company administration.

The initial company creator can manage employees because that account is also explicitly assigned HR Administrator.

---

## Acceptance Criteria

1. Employees can be created.
2. Employees can be updated.
3. Employee lifecycle state is tracked.
4. Manager hierarchy is supported.
5. Position assignment is supported.
6. Employment history is preserved.
7. Termination workflow is supported.
8. Lifecycle events are audited.
9. Permissions are enforced.
10. Terminated employees are retained historically.
