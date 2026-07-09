import type { Config } from 'tailwindcss'
import frostUiPlaceholder from './src/integrations/template/styles/frostui-plugin.placeholder'

// Set to true once the licensed FrostUI package has been installed and
// src/integrations/template/styles/frostui-plugin.placeholder.ts has been
// swapped for the real plugin. See src/integrations/template/README.md.
const USE_TEMPLATE_PLUGIN = false

const config: Config = {
  content: [
    './index.html',
    './src/core/**/*.{ts,tsx}',
    './src/integrations/**/*.{ts,tsx}',
    './src/*.{ts,tsx}',
  ],
  theme: {
    extend: {},
  },
  plugins: [...(USE_TEMPLATE_PLUGIN ? [frostUiPlaceholder] : [])],
}

export default config
