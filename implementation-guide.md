# AI Implementation Instructions

Before implementing any feature, read the relevant repository guidance.

## Architecture

Read the applicable documents under:

- `specifications/architecture/`

The following are mandatory for all implementation work:

- `specifications/architecture/01-solution-structure.md`
- `specifications/architecture/02-module-boundaries.md`
- `specifications/architecture/03-vertical-slice-architecture.md`
- `specifications/architecture/06-authentication-authorization.md`
- `specifications/architecture/07-testing-strategy.md`
- `specifications/architecture/09-coding-standards.md`
- `specifications/architecture/10-ai-implementation-guardrails.md`

## Product

Read:

- `PRODUCT.md`
- `specifications/product-specifications/00-current-product-decisions.md`
- The relevant feature documents under `specifications/product-specifications/`

Where an older feature document conflicts with the dated current-product decision register, the decision register takes precedence.

## Mandatory rules

- Never create a direct reference from one module implementation project to another.
- Cross-module contracts must follow the module-contract rules in the architecture specifications.
- Never create generic repositories.
- Add meaningful tests for new behaviour and changed boundaries.
- Enforce `company_id` isolation and resource-level authorization on the server.
- Use vertical slices for application features.
- Do not introduce MediatR.
- Do not introduce an outbox unless a module has a concrete reliability requirement and explicitly adopts one.
- Do not infer authorization from UI visibility or from the name of a route parameter.

