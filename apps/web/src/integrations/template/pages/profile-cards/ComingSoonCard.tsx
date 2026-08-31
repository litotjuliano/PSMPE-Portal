import type { IconType } from 'react-icons/lib'

interface ComingSoonItem {
  label: string
  icon: IconType
}

interface ComingSoonCardProps {
  title: string
  icon: IconType
  /** One line explaining what will live here. Must not imply a measured value exists. */
  message: string
  /** Optional row labels (each with its own icon) from the design, shown greyed so the panel's
   *  eventual shape is visible. */
  items?: ComingSoonItem[]
}

/**
 * Placeholder panel for sections whose backing system doesn't exist yet (events, payments, CPD,
 * badges - none of these have an entity, table, or endpoint).
 *
 * Deliberately shows no numbers. "0 Events Attended" would be a factual claim that we counted and
 * found none; there is nothing counting, so every such figure would be fabricated. Each row gets an
 * icon chip and a label with "Not tracked yet" at the end, rather than an em-dash - explicit about
 * *why* there's no value, not just that there isn't one.
 */
export const ComingSoonCard = ({ title, icon: Icon, message, items }: ComingSoonCardProps) => (
  <div className="card h-full">
    <div className="card-header">
      <h6 className="card-title flex items-center gap-2">
        <Icon className="size-4 shrink-0" />
        {title}
      </h6>
    </div>
    <div className="card-body flex flex-col gap-4">
      {items && items.length > 0 && (
        <ul className="flex flex-col">
          {items.map(({ label, icon: ItemIcon }) => (
            <li key={label} className="flex items-center justify-between gap-3 py-2 border-b border-default-100 last:border-b-0">
              <span className="flex items-center gap-2.5 text-default-700">
                <span className="flex items-center justify-center rounded-md size-6.5 bg-default-100 text-default-400 shrink-0">
                  <ItemIcon className="size-3.5" />
                </span>
                {label}
              </span>
              <span className="text-xs italic text-default-400 shrink-0">Not tracked yet</span>
            </li>
          ))}
        </ul>
      )}

      <p className="text-xs text-default-500 mt-auto rounded-lg bg-default-100 px-3 py-2">{message}</p>
    </div>
  </div>
)
