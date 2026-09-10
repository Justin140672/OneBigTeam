# Ticket 7 — Make employee contact details accessible in every form state

Priority: Medium

Status: **Open** — the automated coverage below does not substitute for a real assistive-technology
pass or a real browser-zoom pass. See "Outstanding verification".

> The self-service **My Profile → Contact Details** tab (`MyProfileContactDetailsTab.razor` in
> `HR.Web`) must be usable with a keyboard and a screen reader in every state it can reach:
> initial, validation errors, saving, save success, server failure, and optimistic-concurrency
> conflict + reload. The Ticket 2 optimistic-concurrency chrome (`EditSectionBase`,
> `SaveConflictBanner.razor`) is reused and extended rather than duplicated.

---

## Implemented changes

### `MyProfileContactDetailsTab.razor` / `.razor.css` (earlier rounds — unchanged this round)
- Every input has a persistent associated `<label for>` plus a mirrored `aria-label`; each field
  has `aria-describedby` pointing at always-present hint and error containers; required fields
  expose `aria-required="true"`.
- Icon-only dismiss buttons have meaningful `aria-label`s; decorative icons are `aria-hidden`.
- Feedback via ARIA live regions without moving focus: success banner `role="status"` /
  `aria-live="polite"`; server-error banner `role="alert"`; conflict banner `role="alert"`.
- Focus management: client-side validation failure → first invalid field; banner dismissal →
  Save button; concurrency reload → first form field. Server errors / conflicts are announced only.
- Always-rendered polite live region `#cd-saving-status` announces "Saving contact details…" on
  submit of a valid form; `SaveClickedAsync` / `ReloadLatestClickedAsync` use `try/finally` so
  `_saving` and the saving status clear on every path.
- Field rows use `repeat(auto-fit, minmax(220px, 1fr))` + `min-width: 0`; always-visible
  `:focus-visible` outline; empty `.cd-error` containers are hidden.

### Round 3 — test-only server-side save control (this round)

The Contact Details save runs **server-side** (`HR.Web` → `hrapi` via `EmployeeService`), so a
Playwright browser-route interceptor cannot hold, fail, or count it — the previous
`ContactDetailsTab.HeldSave` route interception could not work and its `WaitUntilHeldAsync()` had no
timeout, hanging the tests. Replaced with a control at the real server-side request boundary:

- **`src/HR.Web/Testing/E2eContactSaveControlStore.cs`** — process-wide, `internal`, registered
  **only when `E2E_TESTING=true`** (a flag `HR.AppHost` already forbids outside Development/test
  environments). Holds per-employee-email controls that can hold / release / fail the next outbound
  contact-details `PUT` and count how many arrived. A 60s fail-open bound guarantees the HR.Web
  request thread is never hung even if a test forgets to release.
- **`src/HR.Web/Testing/E2eContactSaveControlHandler.cs`** — `DelegatingHandler` on the `hrapi`
  typed client, inserted **after** `SupabaseAuthDelegatingHandler` and **only when `E2E_TESTING=true`**.
  Acts only on `PUT .../employees/me/contact-details` whose JWT `email` claim matches an armed
  control; everything else passes straight through untouched. The real component, auth, validation
  and `EmployeeService` path stay under test — only the outbound HTTP response is held/synthesised.
- **`src/HR.Web/Program.cs`** — `E2E_TESTING`-gated `/_e2e/contact-save-control/{email}` minimal-API
  endpoints (arm / poll / release / fail / delete) for the test to drive the control; service
  registration and handler wiring, all behind the same flag.
- **`src/Modules/HR.Modules.Employees/EmployeesModule.cs`** + test `SeededE2eEmployees.cs` — four
  dedicated login-less E2E pool employees (`e2e.seed51..54@acme.example`), one per held-save test,
  so each test controls its own employee and is fully isolated by email at `maxParallelThreads=15`.

Test controls are unavailable in a normal/production host: the store, handler and endpoints are
never registered or mapped unless `E2E_TESTING=true`.

### E2E test infrastructure (this round)
- **`tests/HR.Web.E2E.Tests/Infrastructure/PageObjects/ContactDetailsTab.cs`** — removed the
  route-interception `HeldSave` mechanism; added `SaveControl : IAsyncDisposable` that talks to the
  HR.Web control endpoints. Every wait is bounded with a useful message; `DisposeAsync` best-effort
  `DELETE`s the control (releasing any still-held request) so cleanup happens even on assertion
  failure and never affects another test (per-email isolation).
- **`tests/HR.Web.E2E.Tests/Tests/ContactDetailsTabSavingControlTests.cs`** (new) — the held-save
  scenarios, moved out of the accessibility class into a `SupabaseAuthSerialEmployeeTestBase` class
  (real login per test, serialized against other real-Supabase-login tests), one dedicated pool
  employee per test.
- **`tests/HR.Web.E2E.Tests/Tests/ContactDetailsTabAccessibilityTests.cs`** — the three held-save
  tests removed (moved as above); all axe / keyboard / validation / concurrency / narrow-viewport
  tests retained unchanged.

#### Duplicate-submission verification (corrected)
The old test did an ordinary Playwright click on the deliberately-disabled Save button — Playwright
auto-waits for the button to become enabled, which only happens after the response is released, so
the click deadlocked. Corrected flow in
`Saving_AnnouncesToAssistiveTechAndPreventsDuplicateSubmit`:
1. Submit a valid form. 2. Confirm the server-side save is pending (`SaveControl.WaitUntilRequestArrivedAsync`).
3. Assert the saving announcement is present and Save is disabled. 4. Attempt keyboard activation
via `focus()` + `Keyboard.Press("Enter"/"Space")` — **no** Playwright action that waits for the
button to be enabled. 5. Assert exactly one actual save request occurred (`RequestCountAsync() == 1`).
6. Release. 7. Assert success, cleared saving feedback, Save re-enabled.

The component's internal `_saving` guard (`if (_saving) return;`) is not covered by a separate
component test — per project convention bUnit is not used here. It is covered by code review plus
the request-count assertion above.

#### Bounded-failure / cleanup test
`DeliberatelyFailedAssertion_ProducesBoundedFailure_AndLeavesNoPendingRequestOrSharedControl`
proves a wrong expectation / missing request fails within a bounded time (stopwatch-asserted) and
that after disposal the control endpoint returns 404 and a freshly-armed control on the same email
starts clean (`arrived == false`, `requestCount == 0`) — no pending request or shared control left
behind.

---

## Tests written but not executed

E2E tests require a live browser + full Aspire-hosted environment. Written this round, **not run
here** unless recorded under "Tests executed" below:

`tests/HR.Web.E2E.Tests/Tests/ContactDetailsTabSavingControlTests.cs`
- `Saving_AnnouncesToAssistiveTechAndPreventsDuplicateSubmit` — held save: saving live region
  exposed to AT (`role="status"`, `aria-live="polite"`), Save disabled, exactly one request despite
  keyboard re-activation; release clears saving state and shows success; axe scan while held.
- `ServerFailure_ShowsAccessibleErrorAndAllowsRetry` — controlled HTTP 500: `role="alert"` error,
  entered values retained, saving state cleared, Save re-enabled, axe scan; retry against the real
  API succeeds.
- `ErrorBanner_DismissByKeyboard_ReturnsFocusToSave` — keyboard (Space) dismissal of the error
  banner; banner gone, focus back on Save.
- `DeliberatelyFailedAssertion_ProducesBoundedFailure_AndLeavesNoPendingRequestOrSharedControl` —
  see above.

`tests/HR.Web.E2E.Tests/Tests/ContactDetailsTabAccessibilityTests.cs` (retained, unchanged)
- `ContactDetailsTab_InitialState_HasNoSeriousViolations`
- `ContactDetailsTab_ValidationErrorState_HasNoSeriousViolations`
- `ContactDetailsTab_SaveSuccessState_HasNoSeriousViolations`
- `ContactDetailsTab_KeyboardJourney_HasLogicalOrderAndDoesNotYankFocusToBanner`
- `ContactDetailsTab_ValidationFailure_MovesFocusToFirstInvalidField`
- `ContactDetailsTab_ConcurrencyConflict_BannerIsAlert_AndReloadFocusesFirstField`
- `ContactDetailsTab_SuccessBanner_DismissByKeyboard_ReturnsFocusToSave`
- `ContactDetailsTab_FullKeyboardJourney_EntersAndEditsValuesWithRealKeyboardInput`
- `ContactDetailsTab_ValidationCorrection_FocusesFirstInvalidThenSavesAfterKeyboardFix`
- `ContactDetailsTab_NarrowViewport_FieldsValidationAndControlsRemainUsable`
- `ContactDetailsTab_ViewportResize_ApproximatesZoom_AxeClean` (viewport reflow, **not** real zoom)
- `ContactDetailsTab_ConflictState_HasNoSeriousViolations`

Existing `ContactDetailsTabTests.cs` (view/save/validation) — unchanged.

---

## Tests executed

### Build
- `dotnet build` (whole solution): **succeeded**, 0 errors, 9 pre-existing `NU1510` / `xUnit1031`
  warnings (all unrelated).

### Unit / module test projects (`--no-build`)
- `tests/HR.Architecture.Tests` — **Passed** 320 / 0 failed.
- `tests/HR.Modules.Assets.Tests` — **Passed** 235 / 0 failed.
- `tests/HR.Modules.Sickness.Tests` — **Passed** 410 / 0 failed.
- `tests/HR.Web.Tests` — **Passed** 260 / 0 failed.

### Filtered E2E — NOT executed (environment cannot start the Aspire host)
- Command to run (single-class filtered runs are allowed per project convention):
  ```
  dotnet test tests/HR.Web.E2E.Tests/HR.Web.E2E.Tests.csproj --filter "FullyQualifiedName~ContactDetailsTabAccessibilityTests|FullyQualifiedName~ContactDetailsTabSavingControlTests"
  ```
- Attempted twice; both runs failed at fixture start-up with
  `System.IO.InvalidDataException : Service postgres should have valid address at this point`
  (Aspire DCP orchestration could not allocate the Postgres endpoint in this environment — a host
  limitation, not a test failure). All 16 / 4 tests reported "Failed" solely because
  `AppFixture.InitializeAsync` threw before any test body ran.
- **Owner: Justin** — run the command above on a machine where the E2E Aspire host starts and record
  the real pass/fail counts here.

---

## Manual checks completed

None. No manual assistive-technology or real-zoom verification has been performed.

---

## Outstanding verification (owner: Justin)

Not performed and **not** signed off. Ticket 7 stays **Open** until these are done and recorded:

1. **Real 200% browser zoom** — load the form at 200% zoom in a real browser and confirm no
   clipping, overlap, or loss of function of fields, validation messages, and feedback controls.
   The automated harness only resizes the viewport, which is not the same as browser zoom.
2. **NVDA + Firefox** (primary Windows combination) — walk every state: initial, validation errors,
   saving, save success, server failure, concurrency conflict, and reload. Confirm labels, hints,
   required state, and live-region announcements (including "Saving contact details…") are read
   correctly and that focus is never unexpectedly moved.
3. **VoiceOver + Safari** (macOS) — same walkthrough across the same states.

Record the outcome of each on this ticket. Do not mark Ticket 7 verified "in every state" on the
automated tests alone.
