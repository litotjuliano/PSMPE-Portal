import { Outlet } from 'react-router-dom'
import Footer from './Footer'
import Sidebar from './SideNav'
import Topbar from './topbar'
import Customizer from './customizer'
import { usePrelineInit } from '../../hooks/usePrelineInit'
import LayoutProvider from '../../context/useLayoutContext'

const AppShellContent = () => {
  usePrelineInit()

  return (
    <>
      <div className="wrapper">
        <Sidebar />
        <div className="page-content">
          <Topbar />
          <Outlet />
          <Footer />
        </div>
      </div>
      <Customizer />
    </>
  )
}

export const AppShell = () => (
  <LayoutProvider>
    <AppShellContent />
  </LayoutProvider>
)
