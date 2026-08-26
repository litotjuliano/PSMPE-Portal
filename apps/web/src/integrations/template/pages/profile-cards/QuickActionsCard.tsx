import {
  LuAward,
  LuCalendarPlus,
  LuCreditCard,
  LuHeadset,
  LuIdCard,
  LuRefreshCw,
  LuSquarePen,
} from 'react-icons/lu'
import type { IconType } from 'react-icons/lib'

interface QuickActionsCardProps {
  /** Switches the tabs card above to a given tab index (see the 5-tab order in MyProfileTabsCard). */
  onGoToTab: (index: number) => void
}

const SUPPORT_MAILTO = 'mailto:info@psmpe.org?subject=PSMPE%20Portal%20Support'

type Action =
  | { label: string; icon: IconType; onClick: () => void; unavailable?: never; accent?: string }
  | { label: string; icon: IconType; onClick?: never; unavailable: string; accent?: never }

/**
 * The design's seven-button action column. Four of them have no route, endpoint, or backing
 * feature anywhere in the product (there is no renewal flow, digital ID generator, events system,
 * or payments system), so they render disabled with a title explaining why rather than as dead
 * links that appear to work. Real actions each get a distinct accent colour so they stand out from
 * the uniformly muted disabled ones, rather than every enabled action sharing one tint.
 */
export const QuickActionsCard = ({ onGoToTab }: QuickActionsCardProps) => {
  const actions: Action[] = [
    { label: 'Update Profile', icon: LuSquarePen, onClick: () => onGoToTab(0), accent: 'bg-teal text-white hover:bg-teal/90' },
    { label: 'View Certificates', icon: LuAward, onClick: () => onGoToTab(4), accent: 'bg-copper text-white hover:bg-copper/90' },
    { label: 'Renew Membership', icon: LuRefreshCw, unavailable: 'Online renewal is not available yet.' },
    { label: 'Download Digital ID', icon: LuIdCard, unavailable: 'Digital IDs are not available yet.' },
    { label: 'Register for Event', icon: LuCalendarPlus, unavailable: 'Event registration is not available yet.' },
    { label: 'Make Payment', icon: LuCreditCard, unavailable: 'Online payment is not available yet.' },
  ]

  return (
    <div className="card h-full">
      <div className="card-header">
        <h6 className="card-title">Quick Actions</h6>
      </div>
      <div className="card-body flex flex-col gap-4">
        {/* Single column again from xl: this card sits in a narrow track there, where two buttons
            per row would leave each too tight for labels like "Download Digital ID". */}
        <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-1 gap-2">
          {actions.map(({ label, icon: Icon, onClick, unavailable, accent }) => (
            <button
              key={label}
              type="button"
              onClick={onClick}
              disabled={unavailable !== undefined}
              title={unavailable}
              className={`flex items-center gap-2.5 rounded-lg px-3 py-2.5 text-start font-medium transition ${
                unavailable ? 'bg-default-100 text-default-400 cursor-not-allowed' : accent
              }`}
            >
              <Icon className="size-4 shrink-0" />
              <span className="truncate">{label}</span>
            </button>
          ))}

          <a
            href={SUPPORT_MAILTO}
            className="flex items-center gap-2.5 rounded-lg px-3 py-2.5 text-start font-medium transition border border-default-200 text-default-700 hover:bg-default-150"
          >
            <LuHeadset className="size-4 shrink-0" />
            <span className="truncate">Contact Support</span>
          </a>
        </div>

        <p className="text-xs text-default-500 mt-auto">
          Greyed-out actions are features that haven't been built yet.
        </p>
      </div>
    </div>
  )
}
