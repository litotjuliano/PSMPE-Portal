import { Link, useNavigate } from 'react-router-dom'
import { TbSearch } from 'react-icons/tb'
import SimpleBar from 'simplebar-react'
import SidenavToggle from './SidenavToggle'
import ThemeModeToggle from './ThemeModeToggle'
import { LuBadgeCheck, LuBellRing, LuLogOut, LuMoveRight, LuSettings, LuUserPlus, LuUserRound } from 'react-icons/lu'
import { useEffect, useState } from 'react'
import { useAuth } from '../../../../../core/auth/useAuth'
import { Roles } from '../../../../../core/types/auth'
import { memberApi } from '../../../../../core/api/endpoints/memberApi'
import type { Member } from '../../../../../core/types/member'

const Topbar = () => {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  const canReviewApplications = Boolean(user?.roles.includes(Roles.Admin) || user?.roles.includes(Roles.SuperAdmin))
  const [pendingApplications, setPendingApplications] = useState<Member[]>([])
  const [pendingCount, setPendingCount] = useState(0)
  const [pendingPrcVerifications, setPendingPrcVerifications] = useState<Member[]>([])
  const [pendingPrcCount, setPendingPrcCount] = useState(0)

  useEffect(() => {
    if (!canReviewApplications) {
      return
    }
    memberApi
      .getMembers({ pendingApprovalOnly: true, pageSize: 5, sortBy: 'membershipNo' })
      .then((result) => {
        setPendingApplications(result.items)
        setPendingCount(result.totalCount)
      })
    memberApi
      .getMembers({ pendingPrcVerificationOnly: true, pageSize: 5, sortBy: 'membershipNo' })
      .then((result) => {
        setPendingPrcVerifications(result.items)
        setPendingPrcCount(result.totalCount)
      })
  }, [canReviewApplications])

  const totalPendingCount = pendingCount + pendingPrcCount

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
          <ThemeModeToggle />

          <div className="topbar-item hs-dropdown [--auto-close:inside] relative inline-flex">
            <button
              type="button"
              className="hs-dropdown-toggle btn btn-icon size-8 hover:bg-default-150 rounded-full relative"
            >
              <LuBellRing className="size-4.5" />
              {totalPendingCount > 0 && (
                <span className="absolute end-0 top-0 size-1.5 bg-primary/90 rounded-full"></span>
              )}
            </button>
            <div className="hs-dropdown-menu max-w-100 p-0">
              <div className="p-4 border-b border-default-200 flex items-center gap-2">
                <h3 className="text-base text-default-800">Notifications</h3>
                {totalPendingCount > 0 && (
                  <span className="py-0.5 px-2 rounded-full bg-primary/10 text-primary text-xs font-semibold">{totalPendingCount}</span>
                )}
              </div>

              <SimpleBar className="h-80">
                {pendingApplications.map((member) => (
                  <Link
                    key={member.id}
                    to={`/members/${member.id}`}
                    className="flex gap-3 p-4 items-start hover:bg-default-150"
                  >
                    <div className="size-10 rounded-md bg-default-100 flex justify-center items-center">
                      <LuUserPlus className="size-5 text-primary" />
                    </div>
                    <div className="flex justify-between w-full text-sm">
                      <h6 className="font-medium text-default-800">
                        New membership application: {member.firstName} {member.lastName}
                      </h6>
                    </div>
                  </Link>
                ))}
                {pendingPrcVerifications.map((member) => (
                  <Link
                    key={member.id}
                    to="/prc-verifications"
                    className="flex gap-3 p-4 items-start hover:bg-default-150"
                  >
                    <div className="size-10 rounded-md bg-default-100 flex justify-center items-center">
                      <LuBadgeCheck className="size-5 text-primary" />
                    </div>
                    <div className="flex justify-between w-full text-sm">
                      <h6 className="font-medium text-default-800">
                        PRC License verification needed: {member.firstName} {member.lastName}
                      </h6>
                    </div>
                  </Link>
                ))}
                {pendingApplications.length === 0 && pendingPrcVerifications.length === 0 && (
                  <p className="p-4 text-sm text-default-500">No new notifications.</p>
                )}
              </SimpleBar>

              <div className="flex items-center justify-end p-4 border-t border-default-200">
                <Link to="/notifications" className="btn btn-sm text-white bg-primary">
                  View All <LuMoveRight className="size-4" />
                </Link>
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
                <Link
                  to="/profile"
                  className="flex items-center gap-x-3.5 py-1.5 px-3 text-default-600 hover:bg-default-150 rounded font-medium w-full text-left"
                >
                  <LuUserRound className="size-4" />
                  My Profile
                </Link>
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
