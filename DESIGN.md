---
name: One Big Team — Marketing Site
description: A category-standard SaaS marketing register for HR.Marketing — Linear-grade precision fused with Gusto-grade warmth, one confident teal accent, no metaphor.
colors:
  bg: "#fafaf9"
  bg-muted: "#f2f1ed"
  surface: "#ffffff"
  border: "#e6e4de"
  border-strong: "#d4d1c9"
  ink: "#16171b"
  ink-soft: "#55585f"
  ink-faint: "#83858c"
  accent: "#0ea684"
  accent-strong: "#00806a"
  accent-hover: "#00695a"
  accent-soft: "#e1f3ee"
  on-accent: "#ffffff"
  dark: "#14151a"
  dark-surface: "#1d1e25"
  dark-ink: "#f4f4f2"
  danger: "#b3311a"
  danger-soft: "#fbeae6"
typography:
  h1:
    fontFamily: "Inter, Segoe UI, Arial, sans-serif"
    fontSize: "clamp(2.5rem, 3.6vw, 4rem)"
    fontWeight: 700
    lineHeight: 1.05
    letterSpacing: "-0.02em"
  h1-mobile:
    fontFamily: "Inter, Segoe UI, Arial, sans-serif"
    fontSize: "clamp(2.2rem, 12vw, 3.1rem)"
    fontWeight: 700
    lineHeight: 1.05
    letterSpacing: "-0.02em"
  h2:
    fontFamily: "Inter, Segoe UI, Arial, sans-serif"
    fontSize: "clamp(1.8rem, 3vw, 2.4rem)"
    fontWeight: 700
    lineHeight: 1.1
  h3:
    fontFamily: "Inter, Segoe UI, Arial, sans-serif"
    fontSize: "1.06rem"
    fontWeight: 600
  hero-text:
    fontFamily: "Figtree, Segoe UI, Arial, sans-serif"
    fontSize: "clamp(1.1rem, 1.2vw, 1.25rem)"
    fontWeight: 400
  body:
    fontFamily: "Figtree, Segoe UI, Arial, sans-serif"
    fontSize: "16px"
    fontWeight: 400
    lineHeight: 1.6
  brand:
    fontFamily: "Inter, Segoe UI, Arial, sans-serif"
    fontSize: "1.1rem"
    fontWeight: 700
  label:
    fontFamily: "Inter, Segoe UI, Arial, sans-serif"
    fontSize: "0.82rem"
    fontWeight: 700
  meta:
    fontFamily: "Figtree, Segoe UI, Arial, sans-serif"
    fontSize: "0.92rem"
    fontWeight: 400
  price-display:
    fontFamily: "Inter, Segoe UI, Arial, sans-serif"
    fontSize: "clamp(2.6rem, 5.5vw, 4rem)"
    fontWeight: 700
  price-tag:
    fontFamily: "Inter, Segoe UI, Arial, sans-serif"
    fontSize: "clamp(2.1rem, 3.2vw, 2.6rem)"
    fontWeight: 700
  stat-figure:
    fontFamily: "Inter, Segoe UI, Arial, sans-serif"
    fontSize: "clamp(1.9rem, 2.8vw, 2.4rem)"
    fontWeight: 700
    lineHeight: 1
  h2-compact:
    fontFamily: "Inter, Segoe UI, Arial, sans-serif"
    fontSize: "clamp(1.8rem, 2.8vw, 2.3rem)"
    fontWeight: 700
    lineHeight: 1.1
  legal-heading:
    fontFamily: "Inter, Segoe UI, Arial, sans-serif"
    fontSize: "1.3rem"
    fontWeight: 700
  icon-sm:
    fontSize: "1.2rem"
  icon-md:
    fontSize: "1.3rem"
  icon-lg:
    fontSize: "1.6rem"
rounded:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  full: "999px"
spacing:
  sm: "12px"
  md: "20px"
  lg: "36px"
  xl: "72px"
components:
  button-primary:
    backgroundColor: "{colors.accent-strong}"
    textColor: "{colors.on-accent}"
    rounded: "{rounded.sm}"
    padding: "0 20px"
  button-primary-hover:
    backgroundColor: "{colors.accent-hover}"
  button-secondary:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.sm}"
    padding: "0 20px"
  card:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.md}"
    padding: "26px 24px 24px"
---

# Design System: One Big Team — Marketing Site

## Overview

**Direction: category-standard SaaS, played straight.**

The site was rebuilt away from its previous "Company Noticeboard" corkboard identity (brass pins, cork textures, rotated index cards, a single hand-stitched thread accent) into a conventional, restrained SaaS marketing register with no metaphor and no irony. The brief was explicit: fuse Linear's crisp, high-contrast, precisely-spaced typography with Gusto's warm, trustworthy register appropriate for HR/people data. The result reads as *a serious, precise tool for a warm, human subject* — not cold enterprise SaaS, not startup-cute.

Confirmed rejections carried over and reinforced: no gradient hero, no floating dashboard mockup as a crutch, no glass/blur decoration, no neobrutalist offset shadows, no kicker/eyebrow labels above any heading, no cork/paper/pin/thread visual vocabulary anywhere, no monospace-as-costume.

**Key Characteristics:**
- Neutral off-white ground (`--bg`) and near-black ink, carrying a single confident teal accent for every CTA, link, and active state — no second competing hue.
- Cards and panels sit on a clean, modern SaaS radius scale (8–16px), not almost-square corners and not heavy rounding.
- Shadows are soft, neutral-tinted (ink-based, not warm/colored), reading as a UI surface rather than a physical pinned object.
- Inter for display/UI type (headings, buttons, nav, labels), Figtree for body copy — a precise geometric sans paired with a warm, readable humanist body face.
- Header and footer use a grounded near-black dark surface (`--dark`) rather than a wood/cork frame, giving the page clear light/dark structure without any metaphor.

## Colors

### Primary
- **Accent** (`#0ea684`): decorative/icon use, borders, focus rings, hover accents — not used as body-scale text (fails 4.5:1 on white).
- **Accent Strong** (`#00806a`): the text-safe, button-fill variant. Used for primary button backgrounds, links, prices, and any accent-colored text. Verified ≥4.5:1 against both `--bg`/`--surface` (white) and as white-on-accent-strong button fill.
- **Accent Hover** (`#00695a`): hover/active state for accent-strong surfaces.
- **Accent Soft** (`#e1f3ee`): light tint background for secondary-button hover, form-status banners, focus outlines.

### Neutral
- **Bg** (`#fafaf9`) / **Bg Muted** (`#f2f1ed`): page background and muted section bands.
- **Surface** (`#ffffff`): cards, forms, panels.
- **Border** (`#e6e4de`) / **Border Strong** (`#d4d1c9`): card/input borders.
- **Ink** (`#16171b`) / **Ink Soft** (`#55585f`) / **Ink Faint** (`#83858c`): primary, secondary, and tertiary text.

### Dark surfaces
- **Dark** (`#14151a`): header (translucent light variant), footer, final-CTA panel, pricing teaser — the site's one grounded dark region, used consistently rather than scattered.
- **Dark Ink** (`#f4f4f2`) / **Dark Ink Soft** (`rgba(244,244,242,.68)`): text on dark surfaces.

### Status
- **Danger** (`#b3311a`): form validation errors only.

### Named Rules
**The One Accent Rule.** Exactly one committed accent hue (teal) carries every interactive/CTA moment across the site. There is no secondary decorative accent color layered on top.

## Typography

**Display/UI Font:** Inter (headings, buttons, nav, labels, prices — anything that needs precision and confidence).
**Body Font:** Figtree (paragraph copy, captions — warmer, rounder, more approachable for HR/people-facing prose).

Both are real, licensable Google Fonts loaded via `@import`/`<link>` in `App.razor`, with system-sans fallbacks. No monospace face is used anywhere in the system (previously Space Mono stamped prices/labels — removed as monospace-as-costume).

### Hierarchy
- **H1** (700, `clamp(2.5rem, 3.6vw, 4rem)`, line-height 1.05, tracking -0.02em): hero and page-top headlines.
- **H2** (700, `clamp(1.8rem, 3vw, 2.4rem)`, line-height 1.1): section headings.
- **H3** (600, 1.06rem): card and list-item titles.
- **Body** (400, 16px, line-height 1.6, Figtree): paragraph copy; measure capped by container widths (600–760px, within the 65–75ch target).
- **Label** (700, 0.82rem, Inter, tracked 0.02em): pricing-tier labels, footer nav headers — sentence case, no uppercase-as-badge treatment.

### Named Rules
**The No-Kicker Rule** (carried forward, hard requirement): no eyebrow/kicker label ever sits above a heading. The heading carries its own weight through size and weight contrast alone.

## Layout

Content sits in a `min(1160px, 100%)` centered container (`1280px` for the wider feature/pricing grids), with section padding scaling `64px`–`112px` vertically via `clamp()`. The hero is a plain centered content block (headline, subtext, CTA pair) on the page background — no full-bleed frame, no board/panel metaphor — followed by a flat 4-up grid of highlight tiles using the same card component as the rest of the site (no rotation, no pin marks). Card grids default to 3 columns, collapsing to 2 at 980px and 1 at 640px; a lone last card in a 3-column grid centers itself rather than stretching.

## Elevation & Depth

Flat neutral backgrounds carry no shadow; surface elements (cards, forms, buttons, the video card) sit above the page with a soft, neutral ink-tinted shadow — a UI-appropriate shadow vocabulary, not a physical/warm-tinted one.

### Shadow Vocabulary
- **sm** (`0 1px 2px rgba(15,15,20,.05), 0 1px 1px rgba(15,15,20,.04)`): resting state for cards, forms, buttons.
- **md** (`0 6px 16px rgba(15,15,20,.08), 0 2px 6px rgba(15,15,20,.05)`): hover/focus state.
- **lg** (`0 24px 48px -16px rgba(15,15,20,.28), 0 8px 20px -8px rgba(15,15,20,.14)`): the final-CTA dark panel and the video-card play button.

### Named Rules
**The Neutral-Tint Rule.** Shadows are always neutral ink-tinted, never warm/colored — a UI surface casts a UI shadow, not a physical-object shadow.

## Shapes

A modern SaaS radius scale: `8px` (buttons, inputs, small tags), `12px` (cards, table wrappers), `16px` (larger panels — video card, contact form, final-CTA panel), `999px` (pills — step numerals, avatar-style marks). No corner radius in the system is smaller than 8px and none is a hard square.

## Components

### Buttons
- **Shape:** 8px corners, 44px/52px (`.button-large`) min-height.
- **Primary:** `--accent-strong` background, white text, `shadow-sm` at rest; hover shifts to `--accent-hover` and `shadow-md`, `translateY(-1px)`.
- **Secondary:** white surface, `--border-strong` border, ink text; hover swaps border to `--accent-strong` and background to `--accent-soft`.

### Cards / Containers (`.card`)
- **Corner Style:** 12px.
- **Background:** white surface, neutral border.
- **Shadow Strategy:** `shadow-sm` at rest, `shadow-md` on hover/focus-visible, paired with `translateY(-4px)` — a restrained lift, no rotation.
- Every card icon uses a filled 40px rounded-square icon tile in `--accent-soft`/`--accent-strong`, replacing the old brass pin mark as the site's one recurring signature.

### Inputs / Fields
- **Style:** white background, `--border-strong` border, 8px radius, full-width.
- **Focus:** 3px accent-soft outline plus an accent border.
- **Error:** border and message switch to `--danger`; `.is-invalid` reveals the inline error text.

### Navigation
- **Style:** a real, sticky white/translucent nav bar with a hairline bottom border — no wooden frame, no dark cork surface. Mobile collapses to a toggled stacked menu under 980px.
- **Footer:** the site's one dark grounded region (`--dark`), giving light/dark contrast without metaphor.

### Video Card (signature component, unchanged in function)
A click-to-load YouTube facade (real thumbnail + play button; the iframe only mounts on click, so no third-party script loads until then) or an honest "video coming soon" pending state when no recording exists yet. Used for the Home page's product-overview slot and each Feature Detail page's per-feature slot — this remains the site's replacement for a "book a demo" CTA; only its skin changed (white card, 16px radius, accent-strong play button), not its behavior.

## Do's and Don'ts

### Do:
- **Do** keep `--accent-strong` as the only text/CTA-carrying accent color; `--accent` (brighter) stays decorative/icon-only.
- **Do** use Inter for anything that needs precision (headings, buttons, prices, labels) and Figtree for prose — the pairing is deliberate, not inherited.
- **Do** keep shadows neutral ink-tinted and radii on the 8/12/16px scale.
- **Do** keep the video-card click-to-load mechanism as the site's proof/demo device instead of a "book a demo" CTA.

### Don't:
- **Don't** add an eyebrow/kicker label above a heading, ever.
- **Don't** reintroduce cork/paper/pin/thread visual vocabulary, rotation-on-hover, or hand-stamped mono labels — that identity was deliberately retired in favor of a played-straight SaaS register.
- **Don't** use `var(--color-accent)` for body-scale text on light backgrounds — use `var(--color-accent-strong)`, the contrast-verified variant.
- **Don't** introduce gradient text, glass/blur panels, colored side-borders on cards, or hard offset "neobrutalist" shadows.

---

# HR.Web — the application

A separate visual system from the marketing site above: HR.Web is the signed-in Operate-mode app (dashboards, records, grids, forms), built on Bootstrap 5 and Syncfusion Blazor components rather than a custom design language. Its tokens, palette, and type scale are independent of HR.Marketing's — the two are documented side by side in this file rather than forced into one shared system, because they serve different visitor modes (Persuade vs. Operate) and are, deliberately, different code.

```yaml
---
name: One Big Team — Application (HR.Web)
description: A teal-accented Bootstrap/Syncfusion admin system built for task clarity and density, not expression.
colors:
  primary: "#0f766e"
  primary-dark: "#2dd4bf"
  primary-hover: "#115e59"
  primary-hover-dark: "#14b8a6"
  primary-active: "#134e4a"
  primary-active-dark: "#0d9488"
  primary-subtle: "rgba(15, 118, 110, 0.12)"
  primary-subtle-dark: "rgba(45, 212, 191, 0.16)"
  gray-900: "#111827"
  gray-800: "#1f2937"
  gray-700: "#374151"
  gray-500: "#6b7280"
  gray-400: "#9ca3af"
  gray-300: "#d1d5db"
  gray-200: "#e5e7eb"
  gray-100: "#f3f4f6"
  gray-50: "#f9fafb"
  danger: "#dc2626"
  danger-dark: "#f87171"
  warning: "#f97316"
  warning-alt: "#eab308"
  success: "#16a34a"
  sidebar-dark: "#011f3a"
typography:
  h1:
    fontFamily: "Inter, Helvetica Neue, Helvetica, Arial, sans-serif"
    fontWeight: 700
    letterSpacing: "-0.02em"
    lineHeight: 1.2
  body:
    fontFamily: "Inter, Helvetica Neue, Helvetica, Arial, sans-serif"
rounded:
  sm: "4px"
  md: "8px"
  lg: "12px"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "#ffffff"
    rounded: "{rounded.sm}"
  button-primary-hover:
    backgroundColor: "{colors.primary-hover}"
  card:
    backgroundColor: "#ffffff"
    textColor: "{colors.gray-900}"
    rounded: "{rounded.lg}"
---
```

## Overview

**Creative North Star: "The Teal Worktable"**

HR.Web is where the actual work happens — records, approvals, grids, reports — so the system gets out of the way rather than asserting itself. It is Bootstrap 5 plus Syncfusion Blazor components (grids, menus, dropdowns, tabs), re-themed with one consistent teal accent rather than skinned into a bespoke component library. The bar here is scanability and predictable native behavior, not visual distinction; brand shows up in precise, consistent details — one accent color, one radius scale, one shadow vocabulary — layered on top of vendor components rather than replacing them.

Confirmed by the code: full light/dark theme support via `html[data-theme]`, with every token (including shadow opacity) given a real dark variant rather than a naive color inversion. Syncfusion's own dark theme stylesheet is toggled in tandem with the custom tokens, so vendor chrome and custom chrome stay in sync.

**Key Characteristics:**
- One teal accent (`--hr-color-primary`) is pushed into Bootstrap's own CSS variables (`--bs-primary`, `--bs-link-color`, etc.) so every native Bootstrap element inherits it automatically, and individually re-pointed into the highest-traffic Syncfusion controls (buttons, tabs, checkboxes, grid focus rings) that ship pre-baked hex colors instead of variables.
- A functional, undyed neutral gray scale (`gray-50` → `gray-900`) carries structure and text; the teal accent is reserved for interactive/active state, not decoration.
- Light and dark themes are both first-class, toggled by the user (top-bar sun/moon button) and persisted, not just a `prefers-color-scheme` pass-through.
- The sidebar header is a fixed dark navy (`#011f3a`, from the platform logo mark) — the app's one deliberately-branded surface — regardless of light/dark theme.

## Colors

### Primary
- **Teal** (`#0f766e` light / `#2dd4bf` dark): primary buttons, active nav/tab states, focus rings, links, checked checkboxes. ~5.5:1 (light) / ~9:1 (dark) against its typical background — both comfortably past WCAG AA.
- **Teal Hover / Active**: `#115e59`/`#134e4a` (light), `#14b8a6`/`#0d9488` (dark) — state progression for the same interactive elements.
- **Teal Subtle**: `rgba(15,118,110,.12)` / `rgba(45,212,191,.16)` — focus-ring halos and light active-tint backgrounds.

### Neutral
- **Gray 900 → 50**: a standard 9-step scale carrying all text, borders, and surface variation. `gray-900`/`gray-50` are primary text in light/dark; `gray-200`/`gray-700` are the default border color in each theme; `gray-50`/`gray-800` are muted surfaces (card headers, grid headers, hover rows).

### Status
- **Danger** (`#dc2626` / `#f87171` dark), **Warning** (`#f97316`, plus `#eab308` alt), **Success** (`#16a34a`): standard semantic roles for validation, alerts, and status badges — not used decoratively elsewhere.

### Named Rules
**The One Token, Every Surface Rule.** The teal accent is defined once (`--hr-color-primary` and its state variants) and flows into three independent styling systems from that single source: raw CSS classes, Bootstrap's `--bs-*` variables, and targeted Syncfusion component overrides. A new accent-colored element should reference the token, never a literal hex — including inside Syncfusion overrides, where the vendor's own default is hardcoded and must be deliberately re-pointed.

## Typography

**Font:** Inter (with Helvetica Neue/Helvetica/Arial/system-sans fallback) — the only font in the system, used for both UI chrome and body/data text. No separate display or body face; density and hierarchy come from weight and the gray scale, not a font pairing.

### Hierarchy
- **H1** (700, tracked -0.02em, line-height 1.2): page-top headings only; carries a distinctive 4px teal left border (`border-left: 4px solid var(--hr-color-primary)`) as its one signature mark, not used on any other heading level.
- **Body**: Inter at Bootstrap's default scale; data-dense contexts (grid cells, table rows) stay close to Bootstrap defaults rather than introducing a separate compact type scale.

## Layout

Standard Bootstrap grid/container conventions (`.row`/`.col-*`) throughout, with a persistent left sidebar (280px, Syncfusion `SfSidebar`, push-type) on desktop (≥768px) collapsing to an overlay on mobile. Content area padding is modest and consistent (`layout-padding-x`: 1.5rem horizontal); density favors fitting real work (grids, forms, reports) over generous whitespace — the opposite instinct from the marketing site's spacing philosophy above, and correctly so for an Operate surface.

## Elevation & Depth

Flat by default; shadow appears only on cards and as focus-ring feedback, not as a page-wide device.

### Shadow Vocabulary
- **sm** (`0 1px 4px rgba(0,0,0,.06)` light / `rgba(0,0,0,.4)` dark): resting card shadow.
- **md** (`0 8px 20px rgba(0,0,0,.08)` light / `rgba(0,0,0,.35)` dark): elevated/hover state.
- **Focus ring** (`0 0 0 4px var(--hr-color-primary-subtle)`): the shared focus treatment across text inputs, checkboxes, and primary buttons — a colored glow rather than a border shift, consistent across native and Syncfusion controls alike.

## Shapes

A small, consistent radius scale — `4px` (sm: badges, small controls), `8px` (md: buttons, inputs), `12px` (lg: cards) — reused directly by both Bootstrap classes and the Syncfusion overrides rather than letting vendor defaults drift from the app's own scale.

## Components

### Buttons
- **Shape:** Bootstrap default height, `--hr-radius-sm` (4px) corners.
- **Primary:** teal fill, white text; hover/active step through the token's hover/active variants. Syncfusion's `.e-btn.e-primary` is explicitly re-pointed to the same tokens (the vendor theme ships its own blue by default).

### Cards / Containers
- **Corner Style:** 12px (`--hr-radius-lg`).
- **Background:** white (light) / `gray-800` (dark), `gray-200`/`gray-700` border.
- **Shadow Strategy:** `sm` at rest; header band uses `gray-50`/translucent-white-on-dark for `.card-header`.

### Grids (HrGrid, signature component)
Every data grid in the app stamps a shared `hr-grid` marker class so header background, row hover, and toolbar button color are themed centrally rather than per-page. Grids scroll internally (`overflow-x: auto` on a non-flex-shrinking container) rather than forcing their parent layout to grow — deliberate, documented behavior for grids embedded in flex/column layouts.

### Navigation
- **Sidebar:** fixed dark navy header (`#011f3a`) holding the toggle and logo/icon mark, with a `SfMenu` below re-skinned transparent so it reads as part of the sidebar rather than a boxed vendor widget. Active/hover states use the teal token, not Syncfusion's default blue.
- **Tabs (SfTab and Bootstrap nav-tabs/nav-pills):** both re-pointed from Bootstrap/Syncfusion's default blue to the teal token — nav-tabs' active state is color+underline only; nav-pills' active state is a solid teal fill with white text, handled as a separate override since a color-only rule doesn't reach a filled pill's background.

## Do's and Don'ts

### Do:
- **Do** define a new accent/status color as a token in `app.css` first, then reference it — never hardcode a hex directly in a page or component stylesheet.
- **Do** give every new token a real dark-theme variant in the `html[data-theme="dark"]` block, including shadow opacity — not just a color swap.
- **Do** re-point Syncfusion's default blue explicitly wherever a new component variant is adopted; the vendor theme does not inherit the app's CSS variables on its own.
- **Do** keep the sidebar header's dark navy fixed regardless of light/dark theme — it's the app's one constant brand surface.

### Don't:
- **Don't** let Syncfusion's default blue (`#0d6efd`-family) show through on any interactive element — every control variant actually in use gets an explicit override.
- **Don't** introduce a second accent color; status colors (danger/warning/success) stay semantic-only, never decorative.
- **Don't** import HR.Marketing's tokens or fonts into this app, or vice versa — the two systems are intentionally independent (see the top of this file).
