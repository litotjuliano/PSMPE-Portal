# Change: Restyle the Left Navigation

## Status

**Implemented and committed** (`591c856 Restyle the left navigation in PSMPE navy`). Design
approved 2026-08-07 against the supplied mockup; code, lint, and build all verified — see
`tasks.md` for what was checked and what remains only spot-checked rather than exhaustively
re-verified against every item in `## 5. Verify`.

## Why

The sidebar still wears the stock Tailwick template's light treatment: a white rail, 14px
steel-grey labels, 18px icons, and an active item marked only by a 10% blue tint. The approved
mockup calls for a deep navy rail with near-white labels, roomier rows, and a solid blue active bar.

Two problems exist in the current styling regardless of the mockup, verified by reading the CSS:

- **Hover and active are visually identical.** `themes.css:122-125` assigns
  `--sidenav-item-hover-color` / `--sidenav-item-hover-bg` exactly the same values as their
  `-active-` counterparts. A hovered item and the current page look the same, so the sidebar gives
  no reliable "you are here" signal while the pointer is anywhere in it.
- **The sidebar is the one surface that ignores the brand palette.** `themes.css:131-136` sets the
  dark option to the template's generic `--color-steel-900`. The PSMPE "Waterworks Professional"
  palette is defined right above it at lines 11-25 and goes unused by the nav.

## What Changes

Styling only. No nav items are added, removed, or reordered; the three section headings
(`Overview` / `Membership` / `CMS`) and the `requiredRoles` gating in
`apps/web/src/integrations/template/components/layout/SideNav/menu.ts` are untouched.

**1. Navy is wired in by retuning the existing dark token block**, not by adding a third theme or
hardcoding colours on `.app-menu`. The `data-sidenav-color` mechanism and its Light/Dark radios in
the theme customizer keep working; "Dark" simply now means PSMPE navy instead of generic steel.

| Token | Before | After |
|---|---|---|
| `--sidenav-background` | `steel-900` `#1C2530` | `primary-900` `#082C55` |
| `--sidenav-item-color` | `steel-400` `#8494A3` | `primary-200` `#C3D3E8` (new shade) |
| `--sidenav-item-active-color` | `steel-50` | `white` |
| `--sidenav-item-active-bg` | `steel-800` | `primary-600` `#1B5CA6` |
| `--sidenav-item-hover-color` | `steel-300` | `white` |
| `--sidenav-item-hover-bg` | `steel-800` | `rgb(255 255 255 / 8%)` |

Hover and active now differ: a solid blue bar versus a faint white wash. White on `#1B5CA6` is
5.9:1 and `#C3D3E8` on `#082C55` is ~10:1, both clearing WCAG AA.

`--color-primary-200: #C3D3E8` is a new shade sitting outside the approved palette, added with a
comment following the precedent `--color-steel-950` already set at `themes.css:43-45`.
`primary-100` (`#DCE8F5`) reads too bright for idle labels against navy.

**2. Density matches the mockup** via the four existing geometry tokens: `--sidenav-link-padding-x`
12→16px, `--sidenav-link-padding-y` 8→12px, `--sidenav-item-icon-size` 18→20px,
`--sidenav-item-font-size` 14→15px. Roughly 44px rows. These tokens are already consumed
throughout `_sidenav.css` — including the `sm`-mode flyout label at line 305 — so the change
propagates without further edits.

`.menu-link` currently sets `line-height: var(--sidenav-item-font-size)`, i.e. a ratio of 1.0. That
is already tight and at 15px will clip descenders, so it becomes `1.4`.

**3. The active bar goes full-bleed.** The mockup's blue bar starts at the sidebar's leading edge
and rounds only on its trailing end. `AppMenu.tsx:72` applies `p-3` to the `<ul>`, which is the sole
reason items sit inset; it becomes `py-3`. The radius moves off the shared `.menu-link` rule onto
the top-level item as `rounded-s-none rounded-e-md`, using logical properties so `dir="rtl"`
mirrors correctly — the codebase uses `ms`/`me`/`ps`/`pe` throughout and a physical
`border-radius` would break RTL. The active label also gains `font-semibold`.

**4. Navy becomes the default.** `INIT_STATE.sidenav.color` in `useLayoutContext.tsx:32` flips
`light` → `dark`. On its own this reaches nobody who has used the portal before: settings persist to
localStorage under `__PSMPE_LAYOUT_CONFIG__` (line 49) and an existing entry always wins over
`INIT_STATE`. The key is bumped to `__PSMPE_LAYOUT_CONFIG_V2__` so existing sessions fall back to
the new default.

## Impact

- Affected specs: none. This is presentation-layer only — no API surface, no capability change.
- Affected code, all under `apps/web/src/integrations/template/`:
  - `assets/css/themes.css` — the `@theme` palette and geometry tokens, the Dark Menu block
  - `assets/css/structure/_sidenav.css` — link radius, line-height, active weight, sub-menu indent
  - `components/layout/SideNav/AppMenu.tsx` — one className
  - `context/useLayoutContext.tsx` — default sidenav colour, localStorage key
- No backend, database, or build-config changes. No new dependencies.
- The Light/Dark customizer radios and `SidenavColor.tsx` are unchanged.
- No logo work needed: `_sidenav.css:414-423` already swaps `.logo-light` in under
  `html[data-sidenav-color='dark']`, so the white PSMPE wordmark appears automatically.

## Rejected Alternatives

- **Hardcode navy on `.app-menu` and drop the light option.** Fewest lines, but it strands the
  `data-sidenav-color` machinery, requires gutting `SidenavColor.tsx`, and throws away a working
  user preference to save a token block.
- **Add navy as a third `SideNavColorType`.** Most flexible, but it widens a union type, the
  customizer UI, and the CSS for a theme nobody has asked to switch away from. Retuning the dark
  block gets the same result with no new surface.
- **Keep the current compact density.** Smallest diff, but the result reads visibly denser than the
  mockup and keeps 34px rows that are tight as touch targets.

## Out of Scope

- **Filled icons on the active item.** The mockup's active home icon is solid while the rest are
  outlines. `react-icons/lu` (Lucide) ships no filled variants, so this needs a second `activeIcon`
  field on `MenuItemType`, an icon-set change in `menu.ts`, and a branch in `AppMenu.tsx` — a
  structural change, not a styling one. The active icon stays a white outline on the blue bar.
- **The mockup's 21-item flat menu.** Most of those destinations (Digital ID, CPD Learning Center,
  Supplier Directory, Job Opportunities, Community Forum, Media Gallery, …) have no routes, pages,
  or backend. Adding them as dead links is its own change with its own scope.
- **Flattening the section headings.** The mockup shows no headings; the live menu keeps them.
- **The light sidebar's identical hover/active tint.** The same defect described above also exists
  in the Light Menu block at `themes.css:122-125`. This change deliberately does not touch the light
  tokens, so the defect survives there. Worth a follow-up if the light option stays supported.
