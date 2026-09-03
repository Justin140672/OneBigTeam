# Employee Lifecycle Engineering Specification

## Purpose

The Employee domain manages workforce identity, reporting hierarchy,
employment lifecycle, organizational structure, and employee state.

---

## Aggregate: Employee

### Description

Represents a unique human identity within the platform.

An Employee record survives:

- manager changes
- department transfers
- promotions
- rehires

A rehire creates a new Employment record, not a new Employee.

---

## Entity Definition

### Employee

| Field | Type | Required | Constraints | Notes |
|---|---:|---:|---|---|
| id | UUID | Yes | PK | Stable identity |
| companyId | UUID | Yes | FK Company.id | Tenant boundary |
| firstName | VARCHAR(100) | Yes | trimmed | |
| lastName | VARCHAR(100) | Yes | trimmed | |
| preferredName | VARCHAR(100) | No | | |
| workEmail | VARCHAR(255) | Yes | unique per company | |
| personalEmail | VARCHAR(255) | No | | |
| profilePhotoUrl | TEXT | No | | |
| departmentId | UUID | No | FK Department.id | |
| teamId | UUID | No | FK Team.id | |
| managerEmployeeId | UUID | No | self reference | |
| jobTitle | VARCHAR(255) | Yes | | |
| status | ENUM | Yes | see enum | |
| startDate | DATE | Yes | | |
| endDate | DATE | No | > startDate | |

### Status Enum

- active
- inactive
- terminated

---

## Business Rules

1. managerEmployeeId cannot equal employee.id
2. Work email must be unique within company
3. Terminated employees cannot approve requests
4. Manager hierarchy recalculated on manager change

---

## API Expectations

### Create Employee

POST /api/v1/employees

Request:

```json
{
  "firstName": "Jane",
  "lastName": "Doe",
  "workEmail": "jane@company.com",
  "jobTitle": "Engineer"
}
```

Response:

```json
{
  "id": "uuid",
  "status": "active"
}
```

### Update Manager

PATCH /api/v1/employees/{id}/manager

Triggers:
- manager_changed event
- permission recalculation
- task reassignment

---

## Permission Matrix

### Employee
Can:
- view own profile
- update limited profile fields

Cannot:
- edit employment structure

### Manager
Can:
- view detailed employee records for direct and indirect reports beneath them

Salary visibility for that hierarchy is controlled by `DisplaySalaryOnEmployeeProfile`. Bank, tax and payment-sensitive fields remain separately protected.

### HR Admin
May view and manage employee records company-wide and always has salary access. Destructive or specialised operations may still require their own permission.

### Company Administrator

Company Administrator alone has no detailed employee-record or salary access.

---

## Events

- employee_created
- employee_hired
- manager_changed
- employee_terminated

---

## Audit

Every structural change creates immutable diff records.

Example:

```json
{
  "managerEmployeeId": {
    "before": "uuidA",
    "after": "uuidB"
  }
}
```

---

## Edge Cases

### Circular hierarchy

Prevent:

A → B → C → A

Validation required.

### Manager termination

Tasks dynamically reassigned.
