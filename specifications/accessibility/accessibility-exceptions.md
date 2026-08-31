# Accessibility Exceptions Register (NFR-05)

Justified, time-boxed deviations from the WCAG 2.1 AA quality gate defined in
`accessibility-quality-gate.md`. Every entry has an owner and a review date. An entry is removed
(not just ticked) once resolved.

| ID | Area | Exception | Justification | Owner | Opened | Review by |
|----|------|-----------|---------------|-------|--------|-----------|
| A11Y-EX-001 | Forms | `HrValidationSummary` (`role="alert"`, `aria-live="assertive"`, `aria-invalid` on fields) is applied to the Request Leave dialog and the leave policy / leave type edit forms only, not yet to all ~50 `<EditForm>` screens. | Before NFR-05 the app announced no validation errors at all. The shared component + a representative rollout closes the highest-traffic paths; a blanket sweep of every form is a larger mechanical change best done as its own ticket to keep this one reviewable. Remaining forms still show inline `.invalid-feedback` visually. | Web team | 2026-08-31 | 2026-10-15 |
| A11Y-EX-002 | Tooling | The pure impact-filter `AccessibilityScan.SelectBlocking` has no unit test. | It lives in `HR.Web.E2E.Tests`, which references Aspire (`HR.AppHost`) and Playwright; no runnable unit-test project references that assembly, and adding one (or a project reference from `HR.Web.Tests`) would pull the Aspire/Playwright graph into the fast unit suite. Logic is a 3-line case-insensitive `serious`/`critical` filter, exercised end-to-end by every axe scan test. | Web team | 2026-08-31 | 2026-11-30 |
| A11Y-EX-003 | Keyboard | Recruitment Kanban board has no full keyboard drag-and-drop. | Inherited from DSH-07 (explicitly out of scope there). Card data is reachable read-only via Tab; moving a candidate stage is also possible from the candidate detail page without DnD. | Recruitment squad | 2026-07-14 | 2026-12-01 |
| A11Y-EX-004 | Screen reader | HR and Manager dashboard partial-data-failure messages are announced per-widget (`role="alert"`) rather than aggregated into one page-level live region (Recruitment does aggregate). | Inherited from DSH-07. Both compose self-contained child widgets; a single page-level failure summary needs a shared load-orchestration change. Per-widget alerts are still announced. | Web team | 2026-07-14 | 2026-12-01 |
| A11Y-EX-005 | Screen reader | Live screen-reader announcement-quality checks in `accessibility-quality-gate.md` are marked TODO(live SR) and not automated. | Automation (axe + Playwright) verifies structural presence of names, roles, live regions and focus behaviour, but cannot judge whether an announcement is intelligible/timely. Requires a manual NVDA + VoiceOver pass. | Web team | 2026-08-31 | 2026-10-31 |

## Process

- Adding an exception requires: a real justification (not "no time"), a named owner, and a review
  date no more than ~3 months out.
- At each review date the owner either resolves it (remove the row) or re-justifies and moves the
  date, recording why in the PR.
- The `a11y-nightly` workflow does **not** read this file; suppressing a specific axe rule in code
  must reference an ID here in a comment.
