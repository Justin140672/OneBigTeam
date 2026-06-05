# 25. Branding Module

## Overview

The Branding module allows companies to personalise the appearance of the HR platform while maintaining a consistent user experience.

Branding is separate from:

- Company Profile
- Company Settings

Branding controls visual presentation only.

---

## Business Objectives

The module shall:

- Support company logos
- Support company colours
- Support email branding
- Support report branding
- Support document branding
- Maintain a consistent UI experience
- Avoid full white-label complexity in v1

---

## Branding Principles

Branding should:

- Be simple to configure
- Be safe to apply
- Be company-specific
- Be consistent across modules

Branding should not:

- Allow custom CSS
- Allow custom JavaScript
- Permit full UI replacement

---

# Branding Assets

## Primary Logo

Used for:

- Application header
- Login screens
- Navigation

Recommended format:

- PNG
- SVG

---

## Small Logo

Used for:

- Compact navigation
- Mobile layouts
- Notifications

---

## Email Logo

Used for:

- Email headers
- Notification emails

---

# Colour Configuration

## Primary Colour

Used for:

- Main navigation
- Primary buttons

## Secondary Colour

Used for:

- Supporting UI elements

## Accent Colour

Used for:

- Highlights
- Status indicators
- Calls to action

---

# CSS Variable Strategy

Branding colours are mapped to CSS variables.

Example:

--brand-primary
--brand-secondary
--brand-accent

This ensures consistency and maintainability.

---

# Email Branding

Email templates should support:

- Company logo
- Company name
- Branded footer

Branding must not break template layouts.

---

# Report Branding

Generated reports may include:

- Company logo
- Company name
- Report title
- Generation timestamp

Applicable to:

- Excel reports
- PDF reports

---

# Document Branding

Generated documents may include:

- Logo
- Header
- Footer

Examples:

- Offer letters
- Employment contracts
- Policy documents

---

# Storage

Branding assets stored in Supabase Storage.

Suggested location:

{companyId}/branding/

Files remain private.

---

# Permissions

## Employee

Can view branding.

Cannot modify branding.

## HR Admin

Can view branding.

Cannot modify branding unless authorised.

## Company Admin

Can:

- Upload logos
- Configure colours
- Manage branding settings

---

# Validation Rules

## Logo Validation

Supported:

- PNG
- JPG
- SVG

Maximum size configurable.

---

## Colour Validation

Colours must be valid:

- HEX values

Example:

#0055AA

---

# Branding Preview

The platform shall provide:

- Live preview
- Email preview
- Theme preview

Before changes are saved.

---

# Audit Requirements

Audit:

- Logo uploaded
- Logo replaced
- Colour changed
- Branding reset

---

# Reporting

Reports:

- Branding changes
- Asset history

---

# Future Enhancements

Potential future features:

- White-label domains
- Multiple themes
- Department branding
- Customer portals

Not included in v1.

---

# Acceptance Criteria

1. Companies can upload logos.
2. Companies can configure colours.
3. Branding applies across the platform.
4. Branding applies to emails.
5. Branding applies to reports.
6. Branding changes are audited.
7. Branding preview is available.
8. Company isolation is enforced.
