# 30. Dashboard & Home Experience

## Overview

The Dashboard provides the primary landing experience for users of the HR platform.

Dashboards should be role-aware and present relevant information, actions, alerts, and work items.

The goal is to minimise clicks and surface the most important information immediately after login.

---

## Business Objectives

The dashboard shall:

- Surface actionable work
- Display important alerts
- Provide operational visibility
- Improve productivity
- Support role-based experiences
- Reduce navigation effort

---

# Dashboard Principles

Dashboards should be:

- Fast
- Relevant
- Actionable
- Permission-aware
- Personalised

Users should only see information they are authorised to access.

---

# Employee Dashboard

## My Tasks

Displays:

- Open tasks
- Due tasks
- Overdue tasks

---

## My Leave

Displays:

- Remaining allowance
- Upcoming leave
- Pending requests

---

## My Documents

Displays:

- Missing documents
- Expiring documents

---

## Notifications

Displays:

- Recent notifications
- Unread count

---

# Manager Dashboard

Includes Employee Dashboard features plus:

---

## Team Summary

Displays:

- Team size
- New starters
- Leavers

---

## Pending Approvals

Displays:

- Leave requests
- Workflow approvals

---

## Team Tasks

Displays:

- Overdue tasks
- Escalated tasks

---

## Team Compliance

Displays:

- Expiring documents
- Probation reviews due

---

# HR Administrator Dashboard

## Employee Overview

Displays:

- Active employees
- New starters
- Leavers

---

## Recruitment Summary

Displays:

- Open vacancies
- Candidates in pipeline
- Offers awaiting response

---

## Compliance Centre

Displays:

- Expiring visas
- Expiring certifications
- Missing documents

---

## Workflow Queue

Displays:

- Outstanding approvals
- Overdue tasks

---

# Company Administrator Dashboard

## Organisation Overview

Displays:

- Employee count
- Department breakdown
- Recruitment activity

---

## System Activity

Displays:

- Recent audit events
- User activity
- Permission changes

---

## Reporting Shortcuts

Provides quick access to:

- Headcount reports
- Compliance reports
- Audit reports

---

# Dashboard Widgets

Supported widgets:

- KPI cards
- Task lists
- Notification panels
- Charts
- Compliance alerts
- Quick actions

---

# Quick Actions

Examples:

- Request leave
- Create employee
- Create vacancy
- Upload document
- Run report

---

# Notifications Integration

Dashboard displays:

- Unread notifications
- High-priority alerts
- Report completion alerts

---

# Search Integration

Global search entry point available from dashboard navigation.

---

# Performance Requirements

Dashboard should:

- Load quickly
- Support caching where appropriate
- Minimise database queries

Target load:

< 2 seconds

for typical users.

---

# Accessibility

Dashboard must support:

- Keyboard navigation
- Screen readers
- Responsive layouts

---

# Audit Requirements

Audit:

- Dashboard actions
- Administrative shortcuts
- Report generation actions

---

# Acceptance Criteria

1. Dashboard is role-aware.
2. Tasks displayed.
3. Notifications displayed.
4. Compliance alerts displayed.
5. Quick actions available.
6. Search accessible.
7. Performance targets achieved.
8. Permission rules enforced.
