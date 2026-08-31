# Accessibility Quality Gate (NFR-05)

Target conformance: **WCAG 2.1 Level AA**.

This is the cross-page generalisation of the dashboard-only review started in
`dashboard-keyboard-screenreader-review.md` (DSH-07). It defines the automated gate, the
representative journeys it covers, the manual screen-reader review checklist, and points to the
justified-exceptions register.

Legend: [x] automated & asserting / [~] partial or asserts desired state (see notes) / [ ] manual only, not yet done.

## Automated gate

| Mechanism | Where | Runs |
|---|---|---|
| axe-core WCAG 2a/2aa scan, fail on **serious + critical** | `tests/HR.Web.E2E.Tests/Infrastructure/AccessibilityScan.cs` (`AssertNoSeriousViolationsAsync`) | `a11y-nightly.yml` |
| Keyboard-only journeys | `KeyboardJourneyTests`, `KeyboardAuthJourneyTests`, `DashboardAccessibilityTests` | `a11y-nightly.yml` |
| Dialog focus trap + restoration | `tests/HR.Web.E2E.Tests/Infrastructure/DialogAccessibility.cs`, `DialogFocusManagementTests` | `a11y-nightly.yml` |
| Validation error summary + field state announced | `ValidationAnnouncementTests` | `a11y-nightly.yml` |
| Chart / icon text alternatives | `ReportChartAccessibilityTests`, `DashboardAccessibilityTests` | `a11y-nightly.yml` |
| Status-badge / severity-indicator contrast | axe contrast rule over views containing `.status-badge` (leave-types grid etc.) | `a11y-nightly.yml` |
| Compile check (broken a11y test caught on every PR) | `ci.yml` -> `e2e-compile` | every PR |

The axe E2E scan is **not** in the PR gate — it needs the full Aspire + headless-browser stack
(20–40 min, historically flaky headless). PR protection is compile-only.

Serious + critical violations are build failures: `AssertNoSeriousViolationsAsync` throws → test
fails → nightly job fails.

## Representative journeys covered

| Journey | Test class | axe | keyboard | Notes |
|---|---|---|---|---|
| Authentication (`/login`, post-login shell) | `LoginAccessibilityScanTests`, `KeyboardAuthJourneyTests` | [x] | [x] | login form tab order + submit via keyboard |
| Employee self-service (My Profile, leave tab) | `EmployeeSelfServiceAccessibilityScanTests` | [x] | [~] | keyboard covered via Request Leave dialog journey |
| Employee administration (list grid, employee edit) | `AccessibilityScanJourneyTests` | [x] | [~] | grid keyboard nav in `KeyboardJourneyTests` |
| Manager / HR / Recruitment dashboards | `AxeCoreDashboardScanTests`, `DashboardAccessibilityTests` | [x] | [x] | tablist ARIA pattern, aria-live, responsive (DSH-07) |
| Forms & validation (leave policy/type edit, Request Leave) | `ValidationAnnouncementTests`, `AccessibilityScanJourneyTests` | [x] | [x] | see exceptions register re: `HrValidationSummary` rollout |
| Dialogs (Request Leave, HrConfirmDialog, note/photo dialog) | `DialogFocusManagementTests` | [x] | [x] | focus trap + restoration |
| Data grids (leave types, employees, assets) | `AccessibilityScanJourneyTests`, `KeyboardJourneyTests` | [x] | [~] | Syncfusion grid arrow-key cell nav |
| Reports (`/reports` catalogue + report pages) | `AccessibilityScanJourneyTests`, `ReportChartAccessibilityTests` | [x] | [ ] | report pages are currently tabular (no charts) |

## Manual screen-reader review checklist

Run with NVDA + Firefox and VoiceOver + Safari. `[ ]` items require a live SR pass — automation
cannot verify announcement quality, only structural presence.

### Authentication
- [x] `/login` has a single `<h1>`; form fields have associated `<label>`s (axe).
- [ ] TODO(live SR): login error is announced when it appears (`.login-error` — confirm it carries an assertive live region or receives focus).
- [x] Submit button reachable and operable by keyboard (`KeyboardAuthJourneyTests`).

### Employee self-service / administration
- [x] Page has `<h1>`; profile tabs use a real tab/tabpanel pattern or headings (axe: no `aria-*` misuse).
- [ ] TODO(live SR): tab change announces the newly shown panel.
- [x] Grids expose column headers (Syncfusion `SfGrid` renders `role="columnheader"`; axe checks).
- [ ] TODO(live SR): row/cell context is intelligible when arrowing through the employee grid.

### Dashboards
- Covered in full by `dashboard-keyboard-screenreader-review.md` (DSH-07). No regressions expected; the axe scan is now shared.

### Forms & validation
- [~] Error summary is a `role="alert"` / `aria-live="assertive"` region — asserted by `ValidationAnnouncementTests` against the `HrValidationSummary` shared component. Rollout across all ~50 forms is tracked in the exceptions register.
- [~] Invalid fields expose `aria-invalid="true"` — asserted on the covered forms.
- [ ] TODO(live SR): on invalid submit, the error summary is announced and focus moves to it (or the first invalid field).

### Dialogs
- [x] Focus moves into the dialog on open; Tab is trapped; Escape / Cancel restores focus to the trigger (`DialogFocusManagementTests`).
- [x] Dialog has an accessible name (`role="dialog"` + name — used by the tests' locators; axe `aria-dialog-name`).
- [ ] TODO(live SR): dialog open is announced and its purpose is clear.

### Charts & icons
- [x] Dashboard charts have a `<details><table>` or `aria-label` data alternative (DSH-07).
- [x] `ReportChartAccessibilityTests` enforces the same for any chart added to a report page.
- [ ] TODO(live SR): meaningful standalone icons (status pips, severity glyphs) have text or `aria-label`; decorative icons are `aria-hidden`.

### Status / severity indicators
- [x] `.status-badge` variants pass contrast (axe) and are not colour-only — DSH-07 verified glyph+word on dashboards; list grids show a text label alongside the badge.

## Ownership & review cadence

- Owner: Web / Platform team.
- The nightly `a11y-nightly` result is reviewed with the e2e-nightly triage.
- Justified exceptions: `accessibility-exceptions.md` — each has an owner and a review date.
- This checklist is revisited whenever a new top-level journey (new hub, new dashboard, new public page) ships.
