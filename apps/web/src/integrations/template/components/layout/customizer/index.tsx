import { TbX } from 'react-icons/tb'
import Simplebar from '../../shared/Simplebar'
import Direction from './Direction'
import FullScreenToggle from './FullScreenToggle'
import Reset from './Reset'
import SidenavColor from './SidenavColor'
import SidenavView from './SidenavView'
import ThemeMode from './ThemeMode'

const Customizer = () => {
  return (
    <div>
      <div
        id="theme-customization"
        className="hs-overlay hs-overlay-open:translate-x-0 hidden bg-card dark:bg-default-100 hs-overlay-open:flex flex-col translate-x-full rtl:-translate-x-full fixed inset-y-0 end-0 bottom-0 transition-all duration-300 transform max-w-sm w-full z-80 overflow-hidden"
      >
        <div className="min-h-16 flex items-center text-default-600 border-b border-dashed border-default-900/10 px-6 gap-3">
          <h5 className="text-base grow">Theme Settings</h5>

          <FullScreenToggle />

          <button
            type="button"
            data-hs-overlay="#theme-customization"
            className="btn size-9 rounded-full btn-sm hover:bg-default-150"
          >
            <TbX className="text-xl" />
          </button>
        </div>

        <Simplebar className="h-full flex-grow overflow-y-auto">
          <div className="divide-y divide-dashed divide-default-200">
            <SidenavView />

            <ThemeMode />

            <Direction />

            <SidenavColor />
          </div>
        </Simplebar>

        <div className="p-4 flex border-t border-dashed border-default-900/10">
          <Reset />
        </div>
      </div>
    </div>
  )
}

export default Customizer
