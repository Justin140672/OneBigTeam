# 28. Search Experience

## Overview

The Search Experience provides fast, permission-aware search capabilities across the HR platform.

The platform uses PostgreSQL full-text search and module-owned search implementations.

Search results must always respect:

- Authentication
- Company isolation
- Permission scopes
- Manager hierarchy rules

---

## Business Objectives

The search experience shall:

- Provide fast search results
- Support employee lookup
- Support recruitment lookup
- Support document lookup
- Support task lookup
- Support filtering
- Respect permissions
- Scale with company growth

---

# Search Principles

Search is:

- Company scoped
- Permission aware
- Module owned
- Fast
- Audited where appropriate

Search must never expose inaccessible data.

---

# Employee Search

Supported fields:

- First name
- Last name
- Full name
- Email
- Employee number
- Job title
- Department

---

## Employee Search Filters

Filters:

- Department
- Position
- Employment status
- Manager
- Location

---

# Recruitment Search

Supported fields:

- Candidate name
- Candidate email
- Vacancy title
- Recruiter
- Stage

---

## Recruitment Filters

Filters:

- Vacancy
- Pipeline stage
- Recruiter
- Date applied

---

# Document Search

Supported fields:

- File name
- Category
- Employee
- Upload date
- Expiry date

---

## Document Filters

Filters:

- Category
- Employee
- Expiring soon
- Uploaded by

---

# Task Search

Supported fields:

- Task title
- Assigned user
- Status
- Priority

---

## Task Filters

Filters:

- Assigned user
- Status
- Due date
- Priority

---

# Search Architecture

The platform uses:

- PostgreSQL Full Text Search
- GIN indexes
- Search vectors

Each module owns its search implementation.

Examples:

Employees
Recruitment
Documents
Tasks

---

# Debounced Search

Search inputs should use:

300ms debounce

to reduce database load.

---

# Search Results

Results should display:

- Entity type
- Title
- Summary
- Navigation link

---

# Permission Evaluation

Permission checks occur before results are returned.

Search must not:

- Return inaccessible entities
- Leak counts
- Leak metadata

---

# Global Search

Future enhancement.

Potential scope:

- Employees
- Documents
- Recruitment
- Tasks
- Leave

Not required for v1.

---

# Search Performance

Target:

- Typical results < 500ms
- Indexed fields only
- Paginated results

---

# Search Auditing

Audit:

- Sensitive search actions
- Administrative searches
- Compliance searches

---

# UX Requirements

## Search Box

Supports:

- Free text search
- Keyboard navigation
- Result highlighting

---

## Filters

Users may:

- Filter
- Sort
- Clear filters

---

## Empty States

Display:

- No results found
- Suggested filters

---

# Reporting

Reports:

- Search activity
- Popular searches
- Failed searches

Future enhancement.

---

# Acceptance Criteria

1. Employee search supported.
2. Recruitment search supported.
3. Document search supported.
4. Task search supported.
5. Search respects permissions.
6. Search respects company boundaries.
7. Results are paginated.
8. Search performance targets achieved.
