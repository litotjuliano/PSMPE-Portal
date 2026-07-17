import type { ReactNode } from 'react'
import type { IconType } from 'react-icons/lib'
import { LuBan, LuCheck, LuDroplet, LuGauge } from 'react-icons/lu'

export type StatusBadgeVariant = 'active' | 'pending' | 'rejected' | 'verified'

const variantConfig: Record<StatusBadgeVariant, { classes: string; icon: IconType }> = {
  active: { classes: 'bg-teal-50 text-teal-dark dark:bg-teal/15 dark:text-teal', icon: LuDroplet },
  pending: { classes: 'bg-warning/10 text-warning dark:bg-warning/15', icon: LuGauge },
  rejected: { classes: 'bg-danger/10 text-danger dark:bg-danger/15', icon: LuBan },
  verified: { classes: 'bg-copper-50 text-copper-dark dark:bg-copper/15 dark:text-copper-light', icon: LuCheck },
}

interface StatusBadgeProps {
  variant: StatusBadgeVariant
  children: ReactNode
}

/** Pill status indicator - Active/Pending/Rejected(/Expired)/PRC Verified, per the waterworks theme. */
export const StatusBadge = ({ variant, children }: StatusBadgeProps) => {
  const { classes, icon: Icon } = variantConfig[variant]
  return (
    <span className={`inline-flex items-center gap-1 py-0.5 px-2.5 rounded-full text-xs font-medium ${classes}`}>
      <Icon className="size-3" />
      {children}
    </span>
  )
}
