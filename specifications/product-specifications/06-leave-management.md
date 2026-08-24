# Leave Management

> Superseded by `14-leave-management.md` and the "Leave management" section of `00-current-product-decisions.md`, which take precedence wherever they conflict with this document (e.g. default leave types, Sick Leave, conflict-warning scope).

## Purpose
Policy-driven leave management with approvals, accrual, carry-over, and TOIL.

## Core Entities
- LeavePolicy
- LeaveType
- LeaveRequest
- LeaveBalance
- TOILTransaction

## Business Rules
- Working schedule aware
- Partial-day support
- Public holiday aware
- Soft conflict warnings only

## APIs
POST /api/v1/leave-requests
POST /api/v1/leave-requests/{id}/approve

## Events
- leave_requested
- leave_approved
- leave_rejected
