import ArabianFlag from '../../../assets/images/flags/arebian.svg'
import FrenchFlag from '../../../assets/images/flags/french.jpg'
import GermanyFlag from '../../../assets/images/flags/germany.jpg'
import ItalyFlag from '../../../assets/images/flags/italy.jpg'
import JapaneseFlag from '../../../assets/images/flags/japanese.svg'
import RussiaFlag from '../../../assets/images/flags/russia.jpg'
import SpainFlag from '../../../assets/images/flags/spain.jpg'
import UsFlag from '../../../assets/images/flags/us.jpg'
import { Link, useNavigate } from 'react-router-dom'
import { TbSearch } from 'react-icons/tb'
import SimpleBar from 'simplebar-react'
import SidenavToggle from './SidenavToggle'
import ThemeModeToggle from './ThemeModeToggle'
import { LuBellRing, LuClock, LuHeart, LuLogOut, LuMoveRight, LuSettings, LuShoppingBag, LuUserRound } from 'react-icons/lu'
import type { ReactNode } from 'react'
import { useAuth } from '../../../../../core/auth/useAuth'

type Language = {
  src: string
  label: string
}

type Tab = {
  id: string
  title: string
  active?: boolean
}

type Notification = {
  type: 'follow' | 'comment' | 'purchase' | 'like'
  icon?: ReactNode
  text: ReactNode
  time: string
  ago: string
  comment?: string
}

// Sample notification feed for visual parity with the demo - not backed by a real
// notifications API yet. TODO: replace with a real endpoint if this becomes a need.
const languages: Language[] = [
  { src: UsFlag, label: 'English' },
  { src: SpainFlag, label: 'Spanish' },
  { src: GermanyFlag, label: 'German' },
  { src: FrenchFlag, label: 'French' },
  { src: JapaneseFlag, label: 'Japanese' },
  { src: ItalyFlag, label: 'Italian' },
  { src: RussiaFlag, label: 'Russian' },
  { src: ArabianFlag, label: 'Arabic' },
]

const tabs: Tab[] = [
  { id: 'tabsViewall', title: 'View all', active: true },
  { id: 'tabsMentions', title: 'Mentions' },
]

const notifications: Record<string, Notification[]> = {
  tabsViewall: [
    {
      type: 'comment',
      icon: <LuHeart className="size-3.5 fill-orange-500" />,
      text: (
        <>
          Welcome to <b>PSMPE Portal</b>
        </>
      ),
      time: 'Just now',
      ago: 'now',
    },
    {
      type: 'purchase',
      icon: <LuShoppingBag className="size-5 text-danger" />,
      text: <>No new activity yet</>,
      time: '',
      ago: '',
    },
  ],
  tabsMentions: [],
}

const Topbar = () => {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  const handleSignOut = () => {
    logout()
    navigate('/login')
  }

  const initials = (user?.displayName ?? user?.email ?? '?').charAt(0).toUpperCase()

  return (
    <div className="app-header min-h-topbar-height flex items-center sticky top-0 z-30 bg-(--topbar-background) border-b border-default-200">
      <div className="w-full flex items-center justify-between px-6">
        <div className="flex items-center gap-5">
          <SidenavToggle />

          <div className="lg:flex hidden items-center relative">
            <div className="absolute inset-y-0 start-0 flex items-center ps-3 pointer-events-none">
              <TbSearch className="text-base" />
            </div>
            <input
              type="search"
              id="topbar-search"
              className="form-input px-12 text-sm rounded border-transparent focus:border-transparent w-60"
              placeholder="Search something..."
            />
          </div>
        </div>

        <div className="flex items-center gap-3">
          <div className="topbar-item hs-dropdown [--placement:bottom-right] relative inline-flex">
            <button className="hs-dropdown-toggle btn btn-icon size-8 hover:bg-default-150 rounded-full relative" type="button">
              <img src={UsFlag} alt="us-flag" className="size-4.5 rounded" />
            </button>
            <div className="hs-dropdown-menu" role="menu">
              {languages.map((lang, i) => (
                <Link
                  key={i}
                  to="#"
                  className="flex items-center gap-x-3.5 py-1.5 px-3 text-default-600 hover:bg-default-150 rounded font-medium"
                >
                  <img src={lang.src} alt={lang.label} className="size-4 rounded-full" />
                  {lang.label}
                </Link>
              ))}
            </div>
          </div>

          <ThemeModeToggle />

          <div className="topbar-item hs-dropdown [--auto-close:inside] relative inline-flex">
            <button
              type="button"
              className="hs-dropdown-toggle btn btn-icon size-8 hover:bg-default-150 rounded-full relative"
            >
              <LuBellRing className="size-4.5" />
              <span className="absolute end-0 top-0 size-1.5 bg-primary/90 rounded-full"></span>
            </button>
            <div className="hs-dropdown-menu max-w-100 p-0">
              <div className="p-4 border-b border-default-200 flex items-center gap-2">
                <h3 className="text-base text-default-800">Notifications</h3>
              </div>

              <nav className="flex gap-x-1 bg-default-150 p-2 border-b border-default-200" role="tablist">
                {tabs.map((tab, i) => (
                  <button
                    key={i}
                    data-hs-tab={`#${tab.id}`}
                    type="button"
                    className={`hs-tab-active:bg-card hs-tab-active:text-primary py-0.5 px-4 rounded font-semibold inline-flex items-center gap-x-2 border-b-2 border-transparent text-xs whitespace-nowrap text-default-500 hover:text-blue-600 ${tab.active ? 'active' : ''}`}
                  >
                    {tab.title}
                  </button>
                ))}
              </nav>

              <SimpleBar className="h-80">
                {tabs.map((tab, i) => (
                  <div key={i} id={tab.id} className={tab.active ? '' : 'hidden'}>
                    {notifications[tab.id]?.map((n, j) => (
                      <Link key={j} to="#" className="flex gap-3 p-4 items-start hover:bg-default-150">
                        <div className="size-10 rounded-md bg-default-100 flex justify-center items-center">{n.icon}</div>
                        <div className="flex justify-between w-full text-sm">
                          <div>
                            <h6 className="mb-2 font-medium text-default-800">{n.text}</h6>
                            {n.time && (
                              <p className="flex items-center gap-1 text-default-500 text-xs">
                                <LuClock className="size-3.5" /> <span>{n.time}</span>
                              </p>
                            )}
                          </div>
                          {n.ago && (
                            <div className="flex items-center gap-2 text-xs text-default-500">
                              <div className="w-1.5 h-1.5 bg-primary rounded-full"></div>
                              {n.ago}
                            </div>
                          )}
                        </div>
                      </Link>
                    ))}
                  </div>
                ))}
              </SimpleBar>

              <div className="flex items-center justify-end p-4 border-t border-default-200">
                <button type="button" className="btn btn-sm text-white bg-primary">
                  View All <LuMoveRight className="size-4" />
                </button>
              </div>
            </div>
          </div>

          <div className="topbar-item">
            <button
              className="btn btn-icon size-8 hover:bg-default-150 rounded-full"
              type="button"
              aria-haspopup="dialog"
              aria-expanded="false"
              aria-controls="theme-customization"
              data-hs-overlay="#theme-customization"
            >
              <LuSettings className="size-4.5" />
            </button>
          </div>

          <div className="topbar-item hs-dropdown relative inline-flex">
            <button className="hs-dropdown-toggle cursor-pointer size-9.5 rounded-full bg-primary/10 flex items-center justify-center text-primary font-semibold">
              {initials}
            </button>
            <div className="hs-dropdown-menu min-w-48">
              <div className="p-2">
                <div className="flex gap-3">
                  <div className="size-12 rounded bg-primary/10 flex items-center justify-center text-primary font-semibold">
                    <LuUserRound className="size-6" />
                  </div>
                  <div>
                    <h6 className="mb-1 text-sm font-semibold text-default-800">{user?.displayName}</h6>
                    <p className="text-default-500">{user?.email}</p>
                  </div>
                </div>
              </div>

              <div className="border-t border-default-200 -mx-2 my-2"></div>

              <div className="flex flex-col gap-y-1">
                <button
                  onClick={handleSignOut}
                  className="flex items-center gap-x-3.5 py-1.5 px-3 text-default-600 hover:bg-default-150 rounded font-medium w-full text-left"
                >
                  <LuLogOut className="size-4" />
                  Sign Out
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

export default Topbar
