# Commercial Template Integration Boundary

This folder is the **only** place in the frontend allowed to reference a commercial
template or the paid FrostUI Tailwind kit. Nothing under `src/core/` may import from
here, and nothing here may be assumed to exist by `core/` — the app must build and run
with this folder completely empty, as it is today.

## Why this boundary exists

The core CMS (auth, routing, content management) is built entirely on free, open-source
pieces: Tailwind CSS utility classes, `@headlessui/react`, and `@heroicons/react`. This
keeps the starter buildable and testable without any paid assets. When a commercial
template or FrostUI license is available, its components/pages/hooks get dropped in here
instead of scattered across `core/`, so the two remain cleanly separable.

## How to integrate the real package later

1. **FrostUI (Tailwind plugin)**: install the licensed `@frostui/tailwindcss` package,
   then replace `styles/frostui-plugin.placeholder.ts` with the real plugin import and
   flip `USE_TEMPLATE_PLUGIN` to `true` in `../../../tailwind.config.ts`.
2. **Commercial template components/pages**: place them under `components/`, `hooks/`,
   `pages/`, and `utils/` respectively, and re-export whatever core pages should use from
   `index.ts`.
3. **Wiring into core pages**: import from `src/integrations/template` (never deep-import
   a specific file) so the boundary stays enforceable — e.g.
   `import { TemplateButton } from '../../integrations/template'`.
4. Remove the `.gitkeep` files as real content is added.

## TODO

- Once real components exist, add a lint rule (e.g. `eslint-plugin-boundaries` or an
  import restriction) to prevent `core/` from importing `integrations/template` and vice
  versa becoming a two-way dependency.
