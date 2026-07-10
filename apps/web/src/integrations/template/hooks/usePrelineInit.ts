import { useEffect } from 'react'
import { useLocation } from 'react-router-dom'

/**
 * Preline's components are driven by data-hs-* attributes and aren't React-aware,
 * so they need an explicit (re-)init: once after the script loads, and again on
 * every route change so newly-mounted markup gets wired up.
 */
export function usePrelineInit() {
  const location = useLocation()

  useEffect(() => {
    import('preline/preline').then(() => {
      window.HSStaticMethods?.autoInit()
    })
  }, [])

  useEffect(() => {
    window.HSStaticMethods?.autoInit()
  }, [location])
}
