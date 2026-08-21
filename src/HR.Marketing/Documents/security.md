---
title: Security
lastUpdated: 21 August 2026
---

# Security

One Big Team protects customer information through layered security controls. This page
summarises the controls we have in place. We are a UK-based company and design our controls
around UK GDPR and the Data Protection Act 2018 &mdash; see our [Privacy Policy](/privacy-policy)
for how we handle personal data.

## Access control
Access to One Big Team is authenticated and role-based. Users are granted permissions
appropriate to their role within their organisation, and administrative actions are
restricted to authorised users within that organisation.

## Tenant isolation
One Big Team is a multi-tenant platform. Every company's data is kept isolated from every
other company's, and access is scoped to a user's own organisation.

## Encryption
Data is encrypted in transit using HTTPS and by the production hosting providers at rest.
Private documents use restricted storage and time-limited signed access links. Additional
application-level protection for equal-opportunities information is a production launch
requirement and will be verified before that information is collected.

## Backups
The production database will use the database provider's managed backups. Private files will
be backed up daily to encrypted, private AWS storage in London, independently of the primary
file store, and automatically deleted after 30 days. Restoration will be tested before live
customer files are stored and periodically thereafter.

## Audit logging
Security-relevant activity is logged so that changes can be reviewed and investigated.

## Vulnerability management
We aim to keep our software and its dependencies up to date and to address known
vulnerabilities in a timely manner.

## Incident response
If we become aware of a security incident affecting customer data, we will investigate and
notify affected customers in line with our legal obligations.

## Subprocessors
We use a small number of trusted subprocessors to provide infrastructure and operational
services. A current list is available on our [Subprocessors](/subprocessors) page.

## Formal certifications
We have not yet obtained formal third-party security certifications (such as SOC 2 or ISO
27001) or completed an independent penetration test. We will update this page if that changes.

## Contact
For additional information, contact [security@onebigteam.co.uk](mailto:security@onebigteam.co.uk).
