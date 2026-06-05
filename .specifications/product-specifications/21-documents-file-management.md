# 21. Documents & File Management Module

## Overview

The Documents module provides secure document storage, retrieval, auditing, versioning, and lifecycle management across the HR platform.

Storage is provided by Supabase Storage.
Document metadata is stored in PostgreSQL.

## Business Objectives

- Store employee documents
- Store recruitment documents
- Store company documents
- Store generated reports
- Support document versioning
- Support expiry tracking
- Support document search
- Support audit history

## Storage Buckets

### employee-documents
- Contracts
- Passports
- Visas
- Certifications

### recruitment-documents
- CVs
- Cover Letters
- Interview Attachments

### company-documents
- Policies
- Handbooks
- Procedures

### generated-reports
- Exports
- Compliance Reports

## Folder Structure

{companyId}/employees/{employeeId}/
{companyId}/recruitment/{candidateId}/
{companyId}/company/
{companyId}/reports/

## Security

All files are private.

Every access requires:
- Authentication
- Company isolation
- Permission validation

## Permissions

### Employee
Can view own documents.

### Manager
Can view direct report documents.

### HR Admin
Can upload, archive and manage documents.

## Versioning

Documents support version history.

Example:
- v1
- v2
- v3

Latest version is active.

## Expiry Tracking

Supported:
- Passport
- Visa
- Certification
- Driving Licence

Reminders:
- 90 days
- 30 days
- 7 days

## Search

Search by:
- File name
- Category
- Employee
- Upload date
- Expiry date

## Audit

Audit:
- Upload
- Download
- View
- Archive
- Restore
- Delete

## Acceptance Criteria

1. Documents can be uploaded.
2. Documents stored in Supabase Storage.
3. Metadata stored in PostgreSQL.
4. Company isolation enforced.
5. Permissions enforced.
6. Version history maintained.
7. Search supported.
8. Reporting supported.
9. Audit history maintained.
10. Soft delete supported.
