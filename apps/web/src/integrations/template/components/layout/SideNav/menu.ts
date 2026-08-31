import type { IconType } from 'react-icons/lib'
import {
  LuBanknote,
  LuBellRing,
  LuCalendarClock,
  LuFileClock,
  LuFileText,
  LuMonitorDot,
  LuShieldCheck,
  LuSquareUserRound,
  LuUserRound,
  LuUsers,
} from 'react-icons/lu'

export type MenuItemType = {
  key: string
  label: string
  isTitle?: boolean
  href?: string
  children?: MenuItemType[]

  icon?: IconType
  parentKey?: string
  target?: string
  isDisabled?: boolean
  /** Only rendered for these roles; omit to show to everyone. */
  requiredRoles?: string[]
}

// Trimmed to PSMPE Portal's actual feature set. The full Tailwick demo menu
// (ecommerce, HR, invoicing, chat, mailbox, calendar, other auth styles, layout
// variants, etc.) covers ~80 pages this CMS has no backend for — see
// integrations/template/README.md for what's in the package but not wired up.
export const menuItemsData: MenuItemType[] = [
  {
    key: 'Overview',
    label: 'Overview',
    isTitle: true,
  },
  {
    key: 'Dashboard',
    label: 'Dashboard',
    icon: LuMonitorDot,
    href: '/',
  },
  {
    key: 'Membership',
    label: 'Membership',
    isTitle: true,
  },
  {
    key: 'MyProfile',
    label: 'My Profile',
    icon: LuUserRound,
    href: '/profile',
    // Administrative accounts (Admin/Super Admin/Manager/Accounts) don't have membership
    // profiles - see MembersController.UpdateMyProfile.
    requiredRoles: ['Member'],
  },
  {
    key: 'MyCpd',
    label: 'My CPD',
    icon: LuCalendarClock,
    href: '/my-cpd',
    requiredRoles: ['Member'],
  },
  {
    key: 'Members',
    label: 'Members',
    icon: LuUsers,
    href: '/members',
    requiredRoles: ['Admin', 'Super Admin', 'Approval'],
  },
  {
    key: 'Events',
    label: 'Events',
    icon: LuCalendarClock,
    href: '/events',
  },
  // Membership Approvals and RMP Verifications used to sit here. Both were the same
  // GET /api/members query with a different filter, so they are now tabs on Members. The topbar
  // notification bell is what surfaces "work is waiting" now that the nav no longer does.
  {
    key: 'MembershipFees',
    label: 'Membership Fees',
    icon: LuBanknote,
    href: '/membership-fees',
    requiredRoles: ['Admin', 'Super Admin', 'Approval'],
  },
  {
    key: 'Notifications',
    label: 'Notifications',
    icon: LuBellRing,
    href: '/notifications',
    requiredRoles: ['Admin', 'Super Admin', 'Approval'],
  },
  {
    key: 'CMS',
    label: 'CMS',
    isTitle: true,
  },
  {
    key: 'Content',
    label: 'Content',
    icon: LuFileText,
    href: '/content',
  },
  {
    key: 'Users',
    label: 'Users',
    icon: LuSquareUserRound,
    href: '/admin/users',
    requiredRoles: ['Admin', 'Super Admin', 'Approval'],
  },
  {
    key: 'Roles',
    label: 'Roles & Permissions',
    icon: LuShieldCheck,
    href: '/admin/roles',
    requiredRoles: ['Admin', 'Super Admin', 'Approval'],
  },
  {
    key: 'SystemLogs',
    label: 'System Logs',
    icon: LuFileClock,
    href: '/admin/system-logs',
    requiredRoles: ['Super Admin'],
  },
]
