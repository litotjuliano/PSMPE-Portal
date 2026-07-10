import { TbMoon, TbSun } from 'react-icons/tb'

import { useLayoutContext, type LayoutThemeType } from '../../../context/useLayoutContext'

const ThemeModeToggle = () => {
  const { theme, updateSettings } = useLayoutContext()

  const changeTheme = (newTheme: LayoutThemeType) => {
    updateSettings({ theme: newTheme })
  }

  return (
    <div className="topbar-item">
      <button
        className="btn btn-icon size-8 hover:bg-default-150 transition-[scale,background] rounded-full"
        id="light-dark-mode"
        type="button"
        onClick={() => changeTheme(theme === 'light' ? 'dark' : 'light')}
      >
        <TbSun className="text-xl absolute dark:scale-100 dark:rotate-0 scale-0 rotate-90 transition-all duration-200" />
        <TbMoon className="text-xl absolute dark:scale-0 dark:-rotate-90 scale-100 rotate-0 transition-all duration-200" />
      </button>
    </div>
  )
}

export default ThemeModeToggle
