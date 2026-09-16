# Specifications index

This directory is the canonical home for repository documentation.

## Product and implementation

- `PRODUCT.md` — concise product context and standing product principles.
- `implementation-guide.md` — entry point for implementation work.
- `product-specifications/` — feature requirements, decisions and acceptance criteria.
- `architecture/` — architecture, security, persistence and testing rules.

## Supporting specifications

- `accessibility/` — accessibility requirements, exceptions and quality gates.
- `compliance/` — data-protection procedures and retention inventory.
- `design/` — cross-cutting technical designs that are not yet architecture standards.
- `engineering/` — development, dependency and performance guidance.
- `frontend/` — shared user-interface standards.
- `runbooks/` — operational procedures for deployment, recovery and key management.
- `security/` — security mechanisms and sensitive-data decisions.

## Historical records

- `implemented-tickets/` — detailed implementation records retained for rationale and validation evidence.
- `reviews/` — dated repository reviews and their supporting probes.

Markdown remains outside this directory only when its location is part of how it is consumed:

- `src/HR.Marketing/Documents/` contains website content loaded by the marketing application.
- `memories/repo/` contains tool-owned repository memory rather than product documentation.
