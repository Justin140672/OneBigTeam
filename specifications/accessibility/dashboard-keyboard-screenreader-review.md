# Dashboard Keyboard & Screen-Reader Review (DSH-07)

Scope: the three operational dashboards only — HR (`/dashboard/hr`), Manager (`/dashboard/manager`),
Recruitment (`/dashboard/recruitment`). A generalised, automated quality gate across all pages is
tracked separately under NFR-05.

Legend: [x] verified / implemented, [~] partial / known limitation, [ ] not applicable.

## Keyboard

| Check | HR | Manager | Recruitment |
|---|---|---|---|
| All actionable elements reachable via Tab | [x] | [x] | [x] |
| Actionable cards / queue rows are real `<button>`/`<a>` (not div+onclick) | [x] | [x] | [x] |
| Visible `:focus-visible` outline on queue rows, KPI tiles, chart rows, switcher, tabs, retry | [x] | [x] | [x] |
| Dashboard section tabs: `Tab` moves into the active tab only (roving `tabindex`) | [ ] | [ ] | [x] |
| Tabs: `ArrowLeft` / `ArrowRight` move selection + focus, wrapping | [ ] | [ ] | [x] |
| Tabs: `ArrowUp` / `ArrowDown` aliased to Left / Right | [ ] | [ ] | [x] |
| Tabs: `Home` / `End` jump to first / last | [ ] | [ ] | [x] |
| Tab panel is focusable (`tabindex="0"`) and labelled by its tab | [ ] | [ ] | [x] |
| Kanban board full keyboard DnD | [ ] | [ ] | [~] out of scope for DSH-07 (ticket: "do not implement full keyboard tab behaviour" for the board) |
| No keyboard trap | [x] | [x] | [x] |
| Drill-down / task dialogs return focus to trigger on close | [~] existing behaviour, unchanged by DSH-07 | [~] | [~] |

Pure keyboard logic for the Recruitment tabs is extracted to
`HR.Web/Components/Pages/Dashboards/DashboardTabKeyboard.cs` (`NextIndex(key, currentIndex, tabCount)`)
and unit-tested in `tests/HR.Web.Tests/DashboardTabKeyboardTests.cs`.

## Screen reader

| Check | HR | Manager | Recruitment |
|---|---|---|---|
| Page has an `<h1>` and section `<h2>` headings | [x] | [x] | [x] |
| `role="tablist"` / `tab` / `tabpanel` with `aria-selected`, `aria-controls`, `aria-labelledby` | [ ] | [ ] | [x] |
| Polite `aria-live` region announces load completion | [x] | [x] | [x] |
| Polite `aria-live` announces updated counts | [~] load-complete only | [x] on attention / away-today changes | [x] KPI counts |
| Polite `aria-live` announces partial data failures | [~] per-widget `role="alert"` warnings | [~] per-widget | [x] aggregated "Some information could not be loaded: …" |
| Loading state exposed (`role="status"` spinner + label) | [x] | [x] | [x] |
| Actionable queue rows have descriptive `aria-label` incl. priority word | [x] | [x] | [ ] n/a |
| Charts have a text alternative | [x] hbar charts: full figures in `aria-label`; headcount: labelled buttons + group summary | [ ] n/a | [x] Syncfusion funnel + trend charts: `<details>` data `<table>` alternative |
| Status/severity not conveyed by colour alone | [x] | [x] priority pip = per-level glyph + word in label; warning tile = icon + "(needs attention)" | [x] |

Announcement text is built by pure helpers in
`HR.Web/Components/Pages/Dashboards/DashboardAnnouncements.cs`, unit-tested in
`tests/HR.Web.Tests/DashboardAnnouncementsTests.cs`. The live region is rendered by
`DashboardAnnouncer.razor` (`role="status"`, `aria-live="polite"`, `aria-atomic="true"`,
visually hidden).

## Responsive (no unusable horizontal overflow)

| Width | HR | Manager | Recruitment |
|---|---|---|---|
| Mobile ~375px | [x] grids collapse to 1 col; charts shrink (`min-width:0`) | [x] team-status strip + KPI row wrap | [x] header / actions / toolbar / tabs wrap; list + chart tables scroll inside card (`.dashboard-scroll-x`) |
| Tablet ~768px | [x] 2-col grids | [x] | [x] |
| Desktop | [x] unchanged | [x] | [x] |

Automated coverage: `tests/HR.Web.E2E.Tests/Tests/DashboardAccessibilityTests.cs` (keyboard tab nav,
focus visibility, aria-live presence, responsive overflow) and
`tests/HR.Web.E2E.Tests/Tests/AxeCoreDashboardScanTests.cs` (axe-core wcag2a/wcag2aa scan of the
three dashboards). Both are compile-only in this repo's pipeline; NFR-05 will run and generalise them.

## Open items

- HR / Manager dashboards compose self-contained child widgets; their partial-failure announcements
  remain per-widget (`role="alert"`) rather than aggregated into the page live region. Acceptable for
  DSH-07; revisit if a single page-level failure summary is wanted.
- Dialog focus-return behaviour (task view, metric drill-down) predates DSH-07 and was not modified.
- Kanban board keyboard drag-and-drop is explicitly out of scope.
