# Commercial Template Integration — Tailwick

This folder holds the licensed **Tailwick** admin dashboard template (Envato Market,
v2.2.0, by Themesdesign — `React-TS` distribution), integrated as the app's real UI
layer. It's no longer an empty placeholder scaffold.

## What changed to make this fit

Tailwick's own stack (Tailwind CSS v4, Preline UI, React 19, react-router v7) didn't
match this project's original stack (Tailwind v3, Headless UI/Heroicons, React 18,
react-router-dom v6), so the whole frontend was upgraded to match the template rather
than trying to run two incompatible Tailwind versions side by side. See the git history
for the full migration (deps/build config → assets → layout chrome → dashboard → login →
CMS page re-skins).

## Dependency direction (updated from the original one-way rule)

The original version of this doc said `core/` may import from here but never the
reverse. That held while this folder was empty and hypothetical. Now that a real
template is the app's actual layout/UI:

- `core/` imports **presentational** pieces from here (`AppShell`, `DashboardPage`,
  `LoginPage`, `AdminUsersTable`, `ContentListCard`, `ContentEditCard`,
  `PageBreadcrumb`, `PageMeta`) via the single barrel `index.ts` — never a deep import
  into a specific file.
- A few template files import `core/auth/useAuth` directly (`AppMenu.tsx` for
  role-gating the Users nav item, `topbar/index.tsx` for the signed-in user's
  name/email and Sign Out, `WelcomeUser.tsx` for the dashboard greeting). The
  template needs real session data to render correctly — there's no way around this
  once it's the actual UI, not an optional add-on.

This is intentionally bidirectional now. The TODO below about an import-boundary lint
rule is deferred rather than removed — worth adding once this shape has been stable for
a while, to catch *accidental* new coupling rather than the deliberate coupling above.

## What's here

```
assets/{css,images}/     Tailwick's CSS (verbatim) + a curated image subset
components/layout/       SideNav, topbar, Footer, customizer, AppShell
components/dashboard/    The 10 Ecommerce-dashboard widgets + chart config (data.ts)
components/shared/       PageBreadcrumb, PageMeta, ApexChart/IconifyIcon/Simplebar wrappers
context/                 LayoutContext (sidenav size/color, theme, RTL direction)
hooks/                   usePrelineInit - re-runs Preline's autoInit() on route change
pages/                   DashboardPage, LoginPage, and the CMS re-skin components
                          (AdminUsersTable, ContentListCard, ContentEditCard)
utils/, helpers/         Copied support code (debounce, layout attribute helpers, colors)
types/global.d.ts        Window.HSStaticMethods typing for Preline
```

## What's in the Tailwick package but NOT ported here

Only a focused subset was integrated — layout chrome, the main dashboard, one login
style, and the existing CMS pages re-skinned. Explicitly out of scope for this pass:

- HR management, invoicing, ecommerce product/cart/checkout pages, chat, mailbox,
  calendar, notes
- The other 3 auth styles (boxed/cover/modern) and the other 6 auth flows per style
  (register, logout, reset/create password, two-step verify)
- Layout demo variants (dark sidenav, RTL demo, compact/hover/offcanvas/hidden sidenav
  showcases) — the underlying support for most of these (dark mode, RTL, sidenav size)
  is already wired via `context/useLayoutContext.tsx` and the customizer panel, just not
  exposed as dedicated demo pages
- Landing pages, 404/maintenance/coming-soon/offline pages
- Non-dashboard plugin showcase pages from the HTML distribution (forms, tables, icons,
  maps, other chart types)

Any of these can be ported later following the same pattern: find the source page under
`React-TS/src/app/...` in the licensed package, copy into the relevant subfolder here,
fix `@/` imports to relative paths, adapt any dummy data/actions to real API calls.

## Updating the template later

Tailwick ships 15 distributions in the licensed package (`D:\Envanto Templates\...`) —
`React-TS` is the one actually used. If Themesdesign ships an updated version, diff
against what's copied here file-by-file rather than bulk-overwriting, since several
files were deliberately adapted (see git history for the specifics: real auth wiring,
trimmed nav menu, removed dead links, a couple of upstream bug fixes).
