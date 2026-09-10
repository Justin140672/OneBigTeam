# Ticket 7 — Make employee contact details accessible in every form state

Priority: Medium

> The self-service **My Profile → Contact Details** tab (`MyProfileContactDetailsTab.razor` in
> `HR.Web`) must be usable with a keyboard and a screen reader in every state it can reach:
> initial, validation errors, saving, save success, server failure, and optimistic-concurrency
> conflict + reload. The Ticket 2 optimistic-concurrency chrome (`EditSectionBase`,
> `SaveConflictBanner.razor`) is reused and extended rather than duplicated.

## What changed

### `MyProfileContactDetailsTab.razor`
- Every input now has a persistent, correctly associated `<label for>` plus a mirrored
  `aria-label` (Syncfusion wraps the native input, so `for/id` alone is fragile — same
  belt-and-braces approach already used on `EmployeeEdit`).
- Each field carries `aria-describedby` pointing at an always-present hint container and an
  always-present error container, so the description targets exist before an error is shown.
- Required fields (`Address Line 1`, `City`, `Post Code`) expose `aria-required="true"`.
  Native `required` is deliberately **not** set on the Syncfusion-wrapped input because it
  changes `:invalid` styling/pseudo-class behaviour the existing design and E2E selectors
  do not expect.
- Icon-only buttons (success dismiss, error dismiss) have meaningful `aria-label`s; decorative
  Font Awesome icons are `aria-hidden="true"`.
- Feedback is announced via ARIA live regions without moving focus:
  - success banner: `role="status"` / `aria-live="polite"`
  - server-error banner: `role="alert"`
  - concurrency-conflict banner (`SaveConflictBanner`): `role="alert"`
- Focus behaviour is explicit and implemented:
  - client-side validation failure → focus moves to the first invalid field
    (`hrFocusFirstInvalid` in `app.js`)
  - dismissing the success or error banner → focus returns to the Save button
  - concurrency reload → focus moves to the first form field (Personal Email)
  - server errors / conflicts → announced only, focus is not yanked
- Existing validation, save, and concurrency behaviour is unchanged — only markup, ARIA,
  and focus-management wrappers were added.

### `MyProfileContactDetailsTab.razor.css`
- Field rows switch from a fixed `1fr 1fr` grid to `repeat(auto-fit, minmax(220px, 1fr))`
  and field groups get `min-width: 0`, so nothing clips or overlaps at 200% browser zoom or
  narrow viewport widths.
- Always-visible `:focus-visible` outline on form controls and the icon dismiss button.
- Empty `.cd-error` containers are hidden so they add no visual gap until an error appears.

### `SaveConflictBanner.razor`
- Decorative warning icon marked `aria-hidden="true"` (banner already had `role="alert"`).

### `app.js`
- `hrFocusFirstInvalid(formSelector)` — moves focus to the first `[aria-invalid="true"]` /
  `.validation-message` field after a failed save.
- `hrFocusById(id)` — generic focus helper used to return focus to the Save button.

## Verification

### Automated (Playwright / Chromium)
`tests/HR.Web.E2E.Tests/Tests/ContactDetailsTabAccessibilityTests.cs`:

| Test | Covers |
|------|--------|
| `ContactDetailsTab_InitialState_HasNoSeriousViolations` | axe-core scan, initial state |
| `ContactDetailsTab_ValidationErrorState_HasNoSeriousViolations` | axe-core scan, validation-error state |
| `ContactDetailsTab_SaveSuccessState_HasNoSeriousViolations` | axe-core scan, save-success state |
| `ContactDetailsTab_KeyboardJourney_HasLogicalOrderAndDoesNotYankFocusToBanner` | keyboard-only completion, logical tab order, accessible name on every control, no focus movement into the live region |
| `ContactDetailsTab_ValidationFailure_MovesFocusToFirstInvalidField` | focus moves to first invalid field |
| `ContactDetailsTab_ConcurrencyConflict_BannerIsAlert_AndReloadFocusesFirstField` | conflict banner is `role="alert"`, reload returns focus to first field |

New page-object accessors live in
`tests/HR.Web.E2E.Tests/Infrastructure/PageObjects/ContactDetailsTab.cs`.

Syncfusion combobox interactions (none required for this form today) must use the shared
`DropDownSelector` helper if added later — never hand-rolled. Tests use only concrete
locator/state waits and write unique data where they save, so they are deterministic at
`maxParallelThreads=15`.

- **Browser:** Chromium (Playwright bundled build).
- **Assistive technology (automated):** axe-core WCAG 2.0 A/AA rule set via `AccessibilityScan`.

### Manual (to be done by the user)
A manual screen-reader pass is still recommended and has **not** been performed:
- **NVDA on Firefox** (primary Windows combination), and
- **VoiceOver on Safari** (macOS).

Walk each state (initial, validation errors, saving, save success, server failure, concurrency
conflict + reload) and confirm labels, hints, required state, and live-region announcements are
read correctly and that focus is never unexpectedly moved.
