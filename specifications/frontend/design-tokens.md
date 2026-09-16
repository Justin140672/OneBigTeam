# Design tokens — primary control interaction states

Single source of truth: `src/HR.Web/wwwroot/app.css` (`:root` block at the top, plus the
"Targeted Syncfusion re-skin" section further down). Loaded on every page via
`Components/App.razor`, after Bootstrap's CDN stylesheet and the Syncfusion Bootstrap5 theme, so
these tokens are the last word on primary-control colour.

## Brand colour

- `--hr-color-primary` — One Big Team brand teal (`#0f766e` light / `#2dd4bf` dark). This is the
  only value that should ever be hand-typed as a hex colour for a "primary" affordance; everything
  else below is derived from it.
- `--hr-color-secondary` — the logo's cyan-teal accent, used far less often (secondary buttons/
  badges only).

## Interaction-state tokens

| Token | Meaning | Used by |
|---|---|---|
| `--hr-color-primary` | Default/resting state | `.btn-primary`, `.e-btn.e-primary`, active tabs/pills, links, checkboxes |
| `--hr-color-primary-hover` | Mouse/pointer hover | `.btn-primary:hover`, `.e-btn.e-primary:hover`, breadcrumb link hover |
| `--hr-color-primary-active` | Mouse-down / pressed / selected (`.e-active`) | `.btn-primary:active`, `.e-btn.e-primary:active`, `.nav-pills .nav-link.active` |
| `--hr-color-primary-subtle` | Low-opacity wash for chips, icon backgrounds, and the keyboard focus ring's glow | `.e-btn.e-primary:focus` box-shadow, `.employee-filter-chip` |
| `--hr-focus-outline-color` / `--hr-focus-outline-width` | Keyboard focus-visible outline (never removed for aesthetics — WCAG 2.4.7) | `.btn:focus-visible`, `a:focus-visible` |
| `--hr-focus-ring-color` / `--hr-focus-ring-width` | Box-shadow-style focus ring (used instead of `outline` where a component already has a border/outline of its own, e.g. Syncfusion inputs/buttons) | `.e-btn.e-primary:focus`, `.hr-textbox.e-input-focus` |
| `--hr-color-primary-disabled-bg` / `-border` / `-fg` | Disabled — must read as visually *inert*, not just a duller teal | `.e-btn.e-primary:disabled`, `.btn-primary` (via `--bs-btn-disabled-*`) |
| `--hr-color-primary-loading-bg` / `-fg` | Busy/in-flight (e.g. a submit button showing a spinner while `Disabled="@_busy"` is true) — kept close to the hover shade rather than the muted disabled shade, since a loading control is "already working", not "unavailable" | `.e-btn.e-primary:disabled:has(.spinner-border)` (Login's submit button and similar) |

All of the above have dark-mode overrides in the same file under `html[data-theme="dark"]`.

## Why `.btn-primary` needed an explicit override

Bootstrap's compiled `.btn-primary` class sets its own component-local CSS variables
(`--bs-btn-bg`, `--bs-btn-hover-bg`, `--bs-btn-active-bg`, `--bs-btn-disabled-bg`, ...) to literal
hex values baked in at Bootstrap's build time — they do **not** read from the global `--bs-primary`
custom property at runtime. Overriding `--bs-primary` alone therefore only affects utility classes
(`bg-primary`, `text-primary`, form focus rings) and leaves `.btn-primary`'s hover/active/disabled
states on Bootstrap's default blue. `app.css` resets those component-local variables directly on
`.btn-primary` / `.btn-outline-primary` so every plain Bootstrap primary button/link stays on-brand
in every state. Syncfusion's Bootstrap5 theme has the equivalent problem but ships pre-baked hex
rather than CSS variables at all, so its primary controls (`.e-btn.e-primary`, active tabs,
breadcrumb links, checkboxes) are re-skinned with plain selector overrides instead.

## Adding a new primary-styled component

1. Prefer the existing Bootstrap (`btn btn-primary`) or Syncfusion (`SfButton IsPrimary="true"`)
   primary control — both are already fully themed.
2. If you need a bespoke element that should look "primary", reference the tokens above by name
   (`var(--hr-color-primary)`, `var(--hr-color-primary-hover)`, etc.) rather than hard-coding a hex
   value or copying Bootstrap's default blue.
3. Never remove or shrink the `:focus-visible` outline for aesthetic reasons — if it clashes with a
   component's own layout, adjust `outline-offset` instead of removing the outline.
4. Give disabled and loading states a genuinely different treatment (see the disabled/loading
   tokens above) so users can tell "not clickable yet" apart from "click me" and "working on it".
