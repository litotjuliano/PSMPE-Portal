# Tasks: restyle-left-nav

**Goal:** Give the left navigation the approved navy treatment — deep navy rail, near-white labels,
~44px rows, and a solid blue full-bleed active bar that is clearly distinct from hover.

**Architecture:** Everything routes through the existing `data-sidenav-color` token mechanism. The
`html[data-sidenav-color='dark']` block in `themes.css` is retuned from the template's generic steel
to the PSMPE navy palette, the four `--sidenav-*` geometry tokens are bumped, and `dark` becomes the
default. `_sidenav.css` and `AppMenu.tsx` change only to make the active bar full-bleed.

**Tech Stack:** React 19 + Vite + TypeScript, Tailwind CSS v4 (`@theme` block, `@apply`), Preline UI,
`react-icons/lu`. No test runner is configured in `apps/web`, so verification is `lint` + `build`
plus a browser pass.

**Before starting:** read `proposal.md` in this folder.

---

## 1. Design tokens

**Files:**
- Modify: `apps/web/src/integrations/template/assets/css/themes.css`

- [ ] **1.1 Add the extrapolated palette shade**

In the `@theme` block, directly after `--color-primary-100` (line 12), add:

```css
  /* Not in the approved palette - extrapolated between primary-100 and primary-500 for idle label
     text on the navy sidenav, where primary-100 reads too bright. ~10:1 on primary-900. Same
     precedent as steel-950 below. */
  --color-primary-200: #C3D3E8;
```

- [ ] **1.2 Bump the geometry tokens**

Replace lines 79-82:

```css
  --sidenav-link-padding-x: 16px; /* icon inset once the active bar goes full-bleed */
  --sidenav-link-padding-y: 12px; /* ~44px rows, per the approved mockup */
  --sidenav-item-icon-size: 20px;
  --sidenav-item-font-size: 15px;
```

- [ ] **1.3 Retune the Dark Menu block**

Replace the block at lines 128-137. Keep **both** selectors exactly as they are — the second is what
turns the sidebar dark when the whole app is in dark mode, and dropping it leaves a white rail there:

```css
/* Dark Menu (Dark Mode) - PSMPE navy. Hover is deliberately a white wash rather than a second blue,
   so it can't be mistaken for the active item the way the light block's tokens can. */
html[data-sidenav-color='dark'],
html[data-theme='dark'][data-sidenav-color='light'] {
  --sidenav-background: var(--color-primary-900);
  --sidenav-item-color: var(--color-primary-200);
  --sidenav-item-active-color: var(--color-white);
  --sidenav-item-active-bg: var(--color-primary-600);
  --sidenav-item-hover-color: var(--color-white);
  --sidenav-item-hover-bg: rgb(255 255 255 / 8%);
}
```

No `!important` is needed. This block and the Light Menu block above it both score 0-1-1 on
specificity, and this one comes later, so it wins when `data-sidenav-color='dark'` and
`data-theme='light'` are both set — which is the default after Task 4.

---

## 2. Full-bleed active bar

**Files:**
- Modify: `apps/web/src/integrations/template/assets/css/structure/_sidenav.css`

- [ ] **2.1 Fix the link line-height and drop the shared radius**

In the `.menu-link` rule (lines 21-26), remove `rounded` from the `@apply` list — the radius now
differs between top-level items and sub-menu items, so it no longer belongs on the shared rule — and
change the line-height, which is currently a ratio of 1.0 and will clip descenders at 15px:

```css
      line-height: 1.4;
```

- [ ] **2.2 Make the top-level bar full-bleed**

In the `> .menu-item > .menu-link` block (lines 67-76), add as the first declaration:

```css
      @apply rounded-s-none rounded-e-md;
```

Logical properties, not `border-radius` — the file uses `ms`/`me`/`ps`/`pe` throughout and a
physical radius would put the rounded end on the wrong side under `dir="rtl"`.

- [ ] **2.3 Weight the active label**

In the `&.active > .menu-link` rule (lines 78-81), add:

```css
      @apply font-semibold;
```

- [ ] **2.4 Re-align the sub-menu indent**

`.sub-menu` uses `ps-7.5` (line 86), tuned against the old 12px link padding. Change it to `ps-9` so
the bullet still sits under the parent's icon. No menu item currently has children — this is purely
keeping the CSS coherent for when one does.

- [ ] **2.5 Add the navy gradient**

`.app-menu` uses `bg-(--sidenav-background)`, which compiles to `background-color` and cannot hold a
gradient. Add it as a separate `background-image` so the token stays a flat colour for the places
that read it directly (`.logo-box` at line 212, the `sm` flyout at line 259). Append near the other
`html[data-sidenav-color='dark']` block at the end of the file:

```css
/* The rail deepens toward the bottom, per the mockup. Separate from --sidenav-background because
   that token is also used as a flat fill by the sm-mode flyout and logo box. */
html[data-sidenav-color='dark'] .app-menu {
  background-image: linear-gradient(180deg, var(--color-primary-800) 0%, var(--color-primary-900) 55%);
}
```

If it reads muddy in the browser, delete this rule — flat `primary-900` is a fine result on its own.

---

## 3. Remove the horizontal list padding

**Files:**
- Modify: `apps/web/src/integrations/template/components/layout/SideNav/AppMenu.tsx:72`

- [ ] **3.1 Change `p-3` to `py-3`**

```tsx
    <ul className="side-nav py-3 hs-accordion-group">
```

That 12px horizontal inset is the only thing holding the active bar off the sidebar's edge. Vertical
padding stays. `.menu-title` already uses `px-4`, which now matches the new 16px link padding.

---

## 4. Default to navy

**Files:**
- Modify: `apps/web/src/integrations/template/context/useLayoutContext.tsx:32,49`

- [ ] **4.1 Flip the default**

Line 32, in `INIT_STATE`: `color: 'light'` → `color: 'dark'`.

- [ ] **4.2 Bump the localStorage key**

Line 49: `'__PSMPE_LAYOUT_CONFIG__'` → `'__PSMPE_LAYOUT_CONFIG_V2__'`.

Without this, 4.1 reaches nobody who has loaded the portal before — a persisted `color: 'light'`
always wins over `INIT_STATE`. Bumping the key also resets saved sidenav size, theme, and direction,
which is acceptable; they are cosmetic preferences, not user data.

---

## 5. Verify

- [ ] **5.1 Lint and build**

```bash
cd apps/web && npm run lint && npm run build
```
Expected: PASS, no new warnings. The build is what catches a mistyped Tailwind utility in the
`@apply` edits — an unknown class fails the Tailwind v4 compile rather than silently doing nothing.

- [ ] **5.2 Browser pass**

```bash
cd apps/web && npm run dev
```

Signed in as an Admin, so all three sections render:

1. **Default state** — navy rail, near-white labels, ~44px rows, 20px icons. Clear site localStorage
   first to confirm a genuinely fresh visitor lands on navy.
2. **Active vs hover** — go to `/profile`. "My Profile" shows a solid `#1B5CA6` bar running to the
   sidebar's leading edge, rounded on the trailing end, white semibold label. Hovering a *different*
   item gives a faint white wash, visibly different from the active bar. This is the defect the
   change fixes — confirm it directly rather than assuming.
3. **Section headings** — `Overview` / `Membership` / `CMS` still legible against navy.
4. **All seven sidenav sizes** — customizer (gear icon, top right): `default`, `hover`,
   `hover-active`, `sm`, `md`, `offcanvas`, `hidden`. `sm` (70px) is most at risk — icons must stay
   centred and the hover flyout must keep its navy background. `md` (175px) stacks icon over label
   and must not overflow at the taller row height.
5. **Light sidebar still works** — flip to Light: white rail, dark logo, old tinted hover/active
   (deliberately unchanged).
6. **Dark app theme** — theme Dark + sidenav colour Light: the sidebar must go navy, not white. This
   is the `html[data-theme='dark'][data-sidenav-color='light']` selector from 1.3.
7. **Mobile** — under 768px the offcanvas drawer slides in navy with a working backdrop.
8. **RTL** — Direction → RTL: the bar is full-bleed on the *right*, rounded on the left.

- [ ] **5.3 Commit**

```bash
git add apps/web/src/integrations/template/assets/css/themes.css \
        apps/web/src/integrations/template/assets/css/structure/_sidenav.css \
        apps/web/src/integrations/template/components/layout/SideNav/AppMenu.tsx \
        apps/web/src/integrations/template/context/useLayoutContext.tsx \
        openspec/changes/restyle-left-nav/
git commit -m "Restyle the left navigation in PSMPE navy"
```
