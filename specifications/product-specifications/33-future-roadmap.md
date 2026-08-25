# 33. Future Roadmap

## Overview

This document captures features intentionally deferred beyond the initial release of the HR platform.

Items in this roadmap are not required for MVP delivery but may be considered in future phases based on customer demand, commercial priorities, and technical maturity.

---

# Guiding Principles

Future enhancements should:

- Extend existing modules
- Avoid architectural rework
- Maintain modular boundaries
- Preserve tenant isolation
- Reuse established patterns

---

# Phase 2 Enhancements

## Advanced Permissions

Potential additions:

- Department-level scopes
- Location-level scopes
- Temporary delegated access
- Time-bound permissions

---

## Global Search

Cross-module search across:

- Employees
- Documents
- Recruitment
- Leave
- Tasks

Potential features:

- Search suggestions
- Saved searches
- Recent searches

---

## Advanced Workflow Engine

Potential additions:

- Workflow designer
- Approval chains
- Conditional routing
- Escalation rules
- SLA tracking

---

## Enhanced Reporting

Potential additions:

- Report builder
- Scheduled reports
- Report subscriptions
- Dashboard analytics

---

# Recruitment Enhancements

## Public Recruitment Portal

Potential features:

- Public vacancy pages
- Candidate self-service
- Application forms
- Candidate status tracking

---

## Recruitment Integrations

Potential integrations:

- LinkedIn
- Indeed
- Job boards
- CV parsing providers

---

# Employee Enhancements

## Employee Profile Integrations

Potential features:

- Configurable **Apps** area within My Profile for external employee services
- Company-configured integration cards for services such as benefits, staff recognition, wellbeing, payroll and learning
- Optional dedicated profile tabs for high-priority integrations without requiring application code changes
- Configurable integration name, icon, display order, eligibility and launch behaviour
- Support for secure external links, single sign-on and embedded experiences where the provider permits embedding
- Explicit data scopes, tenant isolation and audit logging for every integration

---

## Company Directory

Potential features:

- Employee-facing company directory available to active employees
- Search and filtering by name, department, location, job title and manager
- Privacy controls governing which contact and profile information is visible
- Direct access from the employee profile through a prominent quick-action button
- A friendly people-finding experience separate from administrative employee reports

---

## Internal Vacancies

Potential features:

- Employee-facing list of published internal vacancies
- Search and filtering by department, location and working arrangement
- Vacancy details presented without recruiter-only administration controls
- Direct access from the employee profile through a prominent quick-action button
- Future support for applying, referring a candidate or registering interest

---

## Performance Management

Potential features:

- Objectives
- Reviews
- Appraisals
- Development plans

---

## Learning & Development

Potential features:

- Training records
- Certifications
- Learning plans
- Course tracking

---

# Documents Enhancements

Potential features:

- Retention policies
- Legal hold
- Automated archival
- OCR indexing
- Document classification

---

# Notification Enhancements

Potential features:

- SMS notifications
- Push notifications
- Microsoft Teams integration
- Slack integration

---

# Branding Enhancements

Potential features:

- White-label domains
- Multiple themes
- Department branding
- Customer-facing branding

---

# Security Enhancements

Potential features:

- SSO providers
- SAML support
- Conditional access
- Row Level Security evaluation

---

# Platform Enhancements

## Mobile Application

Potential future implementation:

- .NET MAUI application
- Employee self-service
- Notifications
- Leave requests

---

## Offline Capability

Potential support for:

- Cached employee information
- Offline document access
- Deferred synchronization

---

# AI Capabilities

Potential future features:

- Recruitment assistance
- CV summarisation
- Policy search assistant
- Employee self-service assistant
- Report insights

---

# Internationalisation

Potential additions:

- Multi-language support
- Country-specific policies
- Country-specific compliance packs
- Regional templates

---

# Integrations

Potential future integrations:

- Benefits providers
- Staff recognition platforms
- Payroll systems
- Accounting systems
- Identity providers
- HR marketplaces
- Government services

---

# Technical Roadmap

Potential technical enhancements:

- Distributed services
- Event streaming
- Dedicated reporting service
- Search service
- Advanced caching

---

# Roadmap Governance

Future roadmap items should be evaluated against:

- Customer demand
- Business value
- Operational complexity
- Security impact
- Architectural fit

---

# Acceptance Criteria

1. Roadmap items are clearly separated from MVP scope.
2. Future features align with architectural principles.
3. Roadmap items do not require significant redesign.
4. Enhancements remain compatible with modular architecture.
