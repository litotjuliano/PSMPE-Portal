import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import type { IconType } from 'react-icons/lib'

export type StandardButtonVariant = 'primary' | 'view' | 'success' | 'warning' | 'danger' | 'secondary'

interface StandardButtonBaseProps {
  variant?: StandardButtonVariant
  icon?: IconType
  loading?: boolean
  loadingLabel?: string
  size?: 'sm' | 'default'
  disabled?: boolean
  type?: 'button' | 'submit'
  className?: string
  children: ReactNode
}

interface StandardButtonAsButtonProps extends StandardButtonBaseProps {
  to?: undefined
  onClick?: () => void
}

interface StandardButtonAsLinkProps extends StandardButtonBaseProps {
  to: string
  onClick?: undefined
}

type StandardButtonProps = StandardButtonAsButtonProps | StandardButtonAsLinkProps

// Reuses the theme color tokens already defined in assets/css/themes.css (--color-primary/
// secondary/success/info/warning/danger) - no new CSS needed. "danger" intentionally maps to this
// app's existing `danger` token (orange, not red) rather than introducing a second red that would
// conflict with how `text-danger`/`bg-danger` are already used everywhere else in the app
// (validation messages, delete-hover states, etc.).
const variantClasses: Record<StandardButtonVariant, string> = {
  primary: 'bg-primary text-white hover:bg-primary/90',
  view: 'bg-info text-white hover:bg-info/90',
  success: 'bg-success text-white hover:bg-success/90',
  warning: 'bg-warning text-white hover:bg-warning/90',
  danger: 'bg-danger text-white hover:bg-danger/90',
  secondary: 'border border-default-200 hover:bg-default-150 text-default-700',
}

/**
 * One button component for every CRUD action across the app - color-coded by intent, consistent
 * icon placement, and shared loading/disabled states. Renders a <Link> when `to` is given,
 * otherwise a <button>, so it covers both icon-only Edit links and action buttons.
 */
export const StandardButton = (props: StandardButtonProps) => {
  const {
    variant = 'primary',
    icon: Icon,
    loading = false,
    loadingLabel,
    size = 'default',
    disabled = false,
    type = 'button',
    className = '',
    children,
  } = props

  const classes = `btn ${size === 'sm' ? 'btn-sm' : ''} ${variantClasses[variant]} disabled:opacity-50 inline-flex items-center gap-2 ${className}`.trim()
  const content = (
    <>
      {Icon && <Icon className="size-4" />}
      {loading ? (loadingLabel ?? 'Loading…') : children}
    </>
  )

  if (props.to !== undefined) {
    return (
      <Link to={props.to} className={classes} aria-disabled={disabled || loading}>
        {content}
      </Link>
    )
  }

  return (
    <button type={type} onClick={props.onClick} disabled={disabled || loading} className={classes}>
      {content}
    </button>
  )
}
