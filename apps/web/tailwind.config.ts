import type { Config } from 'tailwindcss'
import forms from '@tailwindcss/forms'
import typography from '@tailwindcss/typography'
import prelinePlugin from 'preline/plugin'

// Tailwind v4: theme tokens (colors, spacing, fonts) live in the CSS `@theme`
// block inside integrations/template/assets/css/themes.css, not here.
// This file only registers content globs and plugins.
const config: Config = {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  plugins: [forms, typography, prelinePlugin],
}

export default config
