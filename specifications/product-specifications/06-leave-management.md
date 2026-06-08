# Leave Management

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
