# Ticket 7 — Make employee contact details accessible in every form state

Priority: Medium

Status: Open (blocked on outstanding manual screen-reader + real-zoom verification — see Verification)

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

## Follow-up (Ticket 7 remains OPEN)

### Accessible "saving" announcement — `MyProfileContactDetailsTab.razor`
- On submit of a valid form, an always-rendered POLITE live region (`#cd-saving-status`,
  `role="status"` / `aria-live="polite"` / `aria-atomic="true"`, visually hidden) announces
  "Saving contact details…".
- The decorative spinner stays `aria-hidden="true"`; its misleading `role="status"` was removed
  so the new live region is the single announcement source.
- Duplicate-submission protection preserved: Save is `Disabled` while in flight, plus an
  `if (_saving) return;` guard.
- Focus is never moved into the status region.
- `SaveClickedAsync` and `ReloadLatestClickedAsync` now use `try/finally`, so `_saving` and the
  saving status are cleared on **every** path — success, server failure, concurrency conflict,
  and unexpected exception. An unsuccessful op never leaves the form stuck "saving" or blocks a
  retry.
- Validation, persistence, optimistic-concurrency behaviour and the existing success / error /
  conflict feedback are unchanged. No changes to `EditSectionBase`, `SaveConflictBanner`, or any
  other consumer of `EditSectionBase`.

## Verification

> The automated tests below cover the browser/axe-checkable behaviour only. They do **not**
> substitute for a real assistive-technology pass. This form is **not** considered verified "in
> every state" on the strength of the automated tests alone.

### Executed — build
- `dotnet build tests/HR.Web.E2E.Tests/HR.Web.E2E.Tests.csproj` (transitively builds `HR.Web`):
  **succeeded**, 0 errors, 4 warnings (all pre-existing `NU1510` from `HR.SharedKernel`, unrelated).

### Executed — automated browser tests (written; run by the user)
`tests/HR.Web.E2E.Tests/Tests/ContactDetailsTabAccessibilityTests.cs`. The E2E suite is run by
the user, not in this workflow. Existing tests (unchanged):

| Test | Covers |
|------|--------|
| `ContactDetailsTab_InitialState_HasNoSeriousViolations` | axe-core scan, initial state |
| `ContactDetailsTab_ValidationErrorState_HasNoSeriousViolations` | axe-core scan, validation-error state |
| `ContactDetailsTab_SaveSuccessState_HasNoSeriousViolations` | axe-core scan, save-success state |
| `ContactDetailsTab_KeyboardJourney_HasLogicalOrderAndDoesNotYankFocusToBanner` | keyboard-only completion, logical tab order, accessible name on every control, no focus movement into the live region |
| `ContactDetailsTab_ValidationFailure_MovesFocusToFirstInvalidField` | focus moves to first invalid field |
| `ContactDetailsTab_ConcurrencyConflict_BannerIsAlert_AndReloadFocusesFirstField` | conflict banner is `role="alert"`, reload returns focus to first field |

New tests (this follow-up):

| Test | Covers |
|------|--------|
| `ContactDetailsTab_Saving_AnnouncesToAssistiveTechAndPreventsDuplicateSubmit` | held save response: saving live region exposed to AT, Save disabled, no duplicate request; release clears saving state; axe scan in saving state |
| `ContactDetailsTab_ServerFailure_ShowsAccessibleErrorAndAllowsRetry` | controlled HTTP 500: `role="alert"` error, entered values preserved, saving state cleared, retry succeeds; axe scan in server-error state |
| `ContactDetailsTab_SuccessBanner_DismissByKeyboard_ReturnsFocusToSave` | keyboard activation of success-dismiss; banner gone, focus back on Save |
| `ContactDetailsTab_ErrorBanner_DismissByKeyboard_ReturnsFocusToSave` | keyboard activation of error-dismiss; banner gone, focus back on Save |
| `ContactDetailsTab_FullKeyboardJourney_EntersAndEditsValuesWithRealKeyboardInput` | real keyboard typing to enter AND edit values, Tab order, Enter to save, predictable post-save focus |
| `ContactDetailsTab_ValidationCorrection_FocusesFirstInvalidThenSavesAfterKeyboardFix` | focus to first invalid field, keyboard correction, save succeeds |
| `ContactDetailsTab_NarrowViewport_FieldsValidationAndControlsRemainUsable` | ~360px viewport: fields, validation messages, feedback controls visible/usable, no clipping/overlap |
| `ContactDetailsTab_ViewportResize_ApproximatesZoom_AxeClean` | viewport-reflow approximation (NOT real browser zoom) + axe scan |
| `ContactDetailsTab_ConflictState_HasNoSeriousViolations` | axe-core scan in optimistic-concurrency conflict state |

New page-object accessors live in
`tests/HR.Web.E2E.Tests/Infrastructure/PageObjects/ContactDetailsTab.cs` (held/controlled save
responses via Playwright route interception + a released `TaskCompletionSource`; each saving test
writes unique data, so deterministic at `maxParallelThreads=15`).

- **Browser:** Chromium (Playwright bundled build).
- **Assistive technology (automated):** axe-core WCAG 2.0 A/AA rule set via `AccessibilityScan`,
  now also run in the saving, server-error and conflict states.

### OUTSTANDING — manual verification (owner: Justin / the user)
Not performed and **not** signed off. Ticket 7 stays **Open** until these are done:

1. **Real 200% browser zoom** — the automated harness can only resize the viewport, which is not
   the same as browser zoom. Load the form at 200% zoom in a real browser and confirm no
   clipping, overlap, or loss of function of fields, validation messages, and feedback controls.
2. **NVDA + Firefox** (primary Windows combination) — walk every state: initial, validation
   errors, saving, save success, server failure, concurrency conflict, and reload. Confirm
   labels, hints, required state, and live-region announcements (including "Saving contact
   details…") are read correctly and that focus is never unexpectedly moved.
3. **VoiceOver + Safari** (macOS) — same walkthrough across the same states.

Record the outcome of each on this ticket. Do not mark Ticket 7 as verified "in every state"
on the automated tests alone.
