import { LuAlignLeft } from 'react-icons/lu'

import { hideBackdrop, showBackdrop, toggleClassName } from '../../../utils/layout'
import { useLayoutContext, type SideNavSizeType } from '../../../context/useLayoutContext'

const SidenavToggle = () => {
  const { sidenav, updateSettings } = useLayoutContext()
  const { size } = sidenav

  const changeSideNavSize = (newSize: SideNavSizeType) => {
    updateSettings({ sidenav: { ...sidenav, size: newSize } })
  }

  const toggleSidebar = () => {
    if (size === 'offcanvas') {
      // Bug fix: the original template called showBackdrop() unconditionally here,
      // even on the click that *closes* the sidebar - toggleClassName's return value
      // (whether the class ended up present or removed) tells us which case this is,
      // so the backdrop only shows while the sidebar is actually open.
      const isNowOpen = toggleClassName('sidenav-enable')
      if (isNowOpen) {
        showBackdrop()
      } else {
        hideBackdrop()
      }
      return
    } else if (size === 'md') {
      changeSideNavSize('sm')
    } else if (size === 'hidden') {
      changeSideNavSize('default')
    } else {
      changeSideNavSize(size === 'sm' ? 'default' : 'sm')
    }

    toggleClassName('sidenav-enable')
  }

  return (
    <button id="button-toggle-menu" className="btn btn-icon size-8 hover:bg-default-150 rounded" onClick={toggleSidebar}>
      <LuAlignLeft size={20} />
    </button>
  )
}

export default SidenavToggle
