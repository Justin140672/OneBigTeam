# 13. Organisation Structure

## Overview

The Organisation Structure capability defines departments, position profiles and reporting relationships.

It supports org charts, reporting, permission inheritance and approval routing.

---

## Business Objectives

The system shall:

- Support departments
- Support position profiles
- Support manager hierarchy
- Support org chart visualisation
- Support position-based permissions
- Support reporting by department and position

---

## Departments

Departments represent organisational groupings.

Examples:

- HR
- Finance
- Engineering
- Operations
- Sales

### Department Fields

| Field | Required |
|---|---|
| Name | Yes |
| Description | Optional |
| Parent Department | Optional |
| Department Manager | Optional |
| Active Status | Yes |

---

## Position Profiles

Position Profiles represent roles/jobs within the organisation.

Examples:

- HR Administrator
- Engineering Manager
- Software Developer
- Finance Manager
- Recruiter

### Position Profile Fields

| Field | Required |
|---|---|
| Title | Yes |
| Department | Optional |
| Description | Optional |
| Managerial Flag | Yes |
| Default Roles | Optional |
| Active Status | Yes |

---

## Position-Based Permissions

Position Profiles may define default roles.

Examples:

### Manager Position

- Manager role
- Direct report access
- Leave approval capability

### HR Administrator Position

- HR Administrator role
- Company-wide HR access

---

## Manager Hierarchy

The reporting hierarchy is defined by explicit employee manager assignment.

The system must not infer manager relationships only from department ownership.

---

## Org Chart

The platform should support org chart visualisation.

Org chart nodes should show:

- Employee name
- Job title
- Department
- Manager relationship

---

## Permissions

### Employee

Can view public organisation information.

### Manager

Can view reporting chain.

### HR Admin

Can manage departments and position profiles.

### Company Admin

Can manage organisation structure.

---

## Audit Events

Audit:

- Department created
- Department updated
- Position profile created
- Position profile updated
- Manager assigned
- Manager changed

---

## Acceptance Criteria

1. Departments can be created.
2. Departments can be updated.
3. Position profiles can be created.
4. Position profiles can assign default roles.
5. Manager hierarchy is supported.
6. Org chart visualisation is supported.
7. Organisation changes are audited.
8. Permissions are enforced.
