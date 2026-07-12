import type { IconType } from 'react-icons/lib'
import { LuBadgeCheck, LuBellRing, LuFileText, LuMonitorDot, LuShieldCheck, LuSquareUserRound, LuUserCheck, LuUserRound, LuUsers } from 'react-icons/lu'

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
    key: 'Members',
    label: 'Members',
    icon: LuUsers,
    href: '/members',
    requiredRoles: ['Admin', 'Super Admin'],
  },
  {
    key: 'MembershipApprovals',
    label: 'Membership Approvals',
    icon: LuUserCheck,
    href: '/membership-approvals',
    requiredRoles: ['Admin', 'Super Admin'],
  },
  {
    key: 'PrcVerifications',
    label: 'PRC Verifications',
    icon: LuBadgeCheck,
    href: '/prc-verifications',
    requiredRoles: ['Admin', 'Super Admin'],
  },
  {
    key: 'Notifications',
    label: 'Notifications',
    icon: LuBellRing,
    href: '/notifications',
    requiredRoles: ['Admin', 'Super Admin'],
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
    requiredRoles: ['Admin', 'Super Admin'],
  },
  {
    key: 'Roles',
    label: 'Roles & Permissions',
    icon: LuShieldCheck,
    href: '/admin/roles',
    requiredRoles: ['Admin', 'Super Admin'],
  },
]
