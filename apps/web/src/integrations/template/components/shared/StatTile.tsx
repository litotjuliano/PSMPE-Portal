import type { IconType } from 'react-icons/lib'

interface StatTileProps {
  icon: IconType
  label: string
  /** Omit (or pass null) together with `pending` for metrics nothing tracks yet. */
  value?: string | number | null
  sublabel?: string
  /**
   * Renders the tile greyed with an em-dash instead of a number. Use this rather than passing 0
   * for anything the system doesn't measure - a zero asserts "we counted, and it was none", which
   * would be false for the metrics that have no source at all.
   */
  pending?: boolean
  /**
   * Icon circle + icon colour for a real (non-pending) tile, e.g. 'bg-teal/15 text-teal'. Lets a
   * strip of tiles use a distinct accent per stat rather than one uniform tint for everything real.
   * Ignored when `pending` is set - pending tiles always render the same muted grey.
   */
  accent?: string
}

/**
 * Compact KPI tile for stat strips. Extracted from the markup that was inlined in a .map inside
 * the dashboard's ProductOrderDetails - the only tile pattern in the tree, and not a component
 * until now.
 */
export const StatTile = ({ icon: Icon, label, value, sublabel, pending = false, accent = 'bg-primary/10 text-primary' }: StatTileProps) => (
  <div className="flex flex-col items-center gap-1.5 p-3 text-center">
    <div className={`flex items-center justify-center rounded-full size-11 shrink-0 ${pending ? 'bg-default-150' : accent.split(' ')[0]}`}>
      <Icon className={`size-5 ${pending ? 'text-default-400' : accent.split(' ')[1]}`} />
    </div>
    <span className={`font-semibold ${pending ? 'text-default-400' : 'text-default-900'}`}>
      {pending ? '—' : (value ?? '—')}
    </span>
    <span className="text-xs text-default-500 leading-tight">{label}</span>
    {sublabel && <span className="text-xs text-default-400 leading-tight">{sublabel}</span>}
  </div>
)
