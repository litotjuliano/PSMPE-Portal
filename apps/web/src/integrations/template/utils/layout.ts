import type { ElementType } from 'react'

type ToggleDocumentAttributeType = (
  attribute: string,
  value: string,
  remove?: boolean,
  tag?: ElementType,
) => void

export const toggleAttribute: ToggleDocumentAttributeType = (attribute, value, remove, tag = 'html'): void => {
  if (document.body) {
    const element = document.getElementsByTagName(tag.toString())[0]
    const hasAttribute = element.getAttribute(attribute)
    if (remove && hasAttribute) element.removeAttribute(attribute)
    else element.setAttribute(attribute, value)
  }
}

/** Returns whether the class ended up present (true) or removed (false) after toggling. */
export const toggleClassName = (className: string, tag: keyof HTMLElementTagNameMap = 'html'): boolean => {
  if (!document.body) return false
  const element = document.getElementsByTagName(tag.toString())[0]
  return element.classList.toggle(className)
}

export const getSystemTheme = () => {
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

export const showBackdrop = () => {
  // Bug fix: the original template creates a new backdrop on every call with no
  // check for an existing one. Clicking the sidebar toggle more than once while in
  // offcanvas mode (e.g. a narrow viewport, such as with DevTools docked) stacked up
  // duplicate full-screen divs that silently intercepted every click until the page
  // was refreshed - hideBackdrop()'s getElementById only ever removed the first one.
  if (document.getElementById('custom-backdrop')) return

  const backdrop = document.createElement('div')
  backdrop.id = 'custom-backdrop'
  backdrop.className = 'transition duration fixed inset-0 bg-default-900/50 z-40'
  document.body.appendChild(backdrop)
  backdrop.addEventListener('click', () => {
    toggleClassName('sidenav-enable')
    hideBackdrop()
  })
}

export const hideBackdrop = () => {
  document.querySelectorAll('#custom-backdrop').forEach((backdrop) => backdrop.remove())
}
