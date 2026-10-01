# Responsive shell and content layout

## Shell breakpoints

The shell mode is chosen from the viewport width. `ShellBreakpoints` (src/HR.Web/Services/ShellNavState.cs) is the single
definition; `window.hrShell` in app.js reports changes to `MainLayout`.

| Mode | Viewport | Navigation | Content |
|---|---|---|---|
| Wide desktop (`wide`) | 1200px and above | Persistent expanded sidebar (280px). Collapse/expand is a remembered preference. | Multi-column |
| Narrow desktop / tablet landscape (`compact`) | 768px to 1199px | Collapsed by default. No sidebar width is reserved. Full navigation opens as an overlay drawer with a backdrop. | Fewer KPI and filter columns, report cards 1 to 2 columns |
| Tablet portrait and smaller (`overlay`) | below 768px | Overlay drawer only | Filters stack, KPI cards 1 to 2 columns, profile identity and actions stack, tables scroll inside their own container |

`<div class="app-shell">` exposes `data-nav-mode` (`pending`, `wide`, `compact`, `overlay`) and `data-nav-open`
(`true` or `false`). `pending` is only the server-rendered first paint before the browser reports its width; CSS keeps the
sidebar hidden on constrained widths during that moment so no width is reserved.

## Navigation state rules

- The remembered preference is stored in the first-party `navExpanded` local storage entry and only applies in `wide` mode.
- At `compact` and `overlay` widths the sidebar is temporarily collapsed. Opening the drawer never changes the preference.
- Returning to `wide` restores the remembered preference and closes any open drawer.
- Choosing a destination closes the drawer and resets the page horizontal scroll position to zero.
- Escape closes the drawer. Focus moves to the first control inside the drawer when it opens, and returns to the
  "Open navigation" button when it closes. In `wide` mode collapsing moves focus to "Open navigation" and expanding moves
  focus back to "Collapse navigation".
- Implementation: `ShellNavState` (pure state, unit tested), `MainLayout.razor` and `MainLayout.razor.css`.

## Content rules

- Wide components never widen the page. Grids scroll inside `.hr-grid`.
- Use intrinsic sizing instead of assuming the viewport: `.kpi-grid` (auto-fit, 11rem minimum), `.report-filter-grid`
  (auto-fit, 15rem minimum) and `.report-card-grid` (auto-fill, 19rem minimum) reflow from the width their container actually has.
- KPI labels use normal word breaking so words are never split into fragments.
- The employee profile header (`.employee-profile-header`) wraps; the employee number never wraps; header actions move
  below the identity on narrow widths.
- Search boxes use `.page-search-box` (full width up to 30rem).
- Top bar: below 1200px the user name and job title are visually hidden but remain available to assistive technology, and the
  page title wraps instead of truncating.
- Focus indicators on shell controls use a 3px outline with a contrasting offset ring.

## Out of scope

Redesigning the navigation visual style, replacing the grid component, changing report content or calculations, changing
profile Save and Cancel, general mobile feature parity.
