import type { ApexOptions } from 'apexcharts'
import { LuClock, LuUserCheck, LuUserMinus, LuUserX } from 'react-icons/lu'
import ApexChart from '../shared/ApexChart'
import { StatTile } from '../shared/StatTile'
import type { MemberStats } from '../../../../core/api/endpoints/memberApi'

type StatusCounts = MemberStats['statusCounts']

/** Fixed status -> hue mapping, reused for both the StatTile accents and the donut slices so the
 *  same status always reads as the same color across the widget. Validated as a categorical
 *  palette (CVD-safe, on-brand) against the app's PSMPE theme tokens - see themes.css. */
const STATUS_COLOR = {
  pending: '#C77E0F', // --color-warning
  active: '#0F9BA8', // --color-teal / --color-success
  expired: '#B3382E', // --color-danger
  deactivated: '#2E6FB8', // --color-primary-500 / --color-info
} as const

const getStatusDonutOptions = (total: number): ApexOptions => ({
  chart: { type: 'donut' },
  labels: ['Pending', 'Active', 'Expired', 'Deactivated'],
  colors: [STATUS_COLOR.pending, STATUS_COLOR.active, STATUS_COLOR.expired, STATUS_COLOR.deactivated],
  legend: { position: 'bottom', fontSize: '13px' },
  dataLabels: { enabled: false },
  plotOptions: {
    pie: {
      donut: {
        labels: {
          show: true,
          total: { show: true, label: 'Total Members', formatter: () => String(total) },
        },
      },
    },
  },
})

/**
 * Status split for the Membership dashboard - four StatTiles for a quick read, plus a donut for
 * the proportional view. Takes just `statusCounts` (not the whole MemberStats) since that's the
 * only slice of the payload it needs.
 */
export function MembershipStatusBreakdown({ statusCounts }: { statusCounts: StatusCounts }) {
  const { pending, active, expired, deactivated } = statusCounts
  const total = pending + active + expired + deactivated

  return (
    <div className="card h-full">
      <div className="card-header">
        <h6 className="card-title">Membership Status</h6>
      </div>
      <div className="card-body">
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 mb-3">
          <StatTile icon={LuClock} label="Pending" value={pending} accent="bg-warning/15 text-warning" />
          <StatTile icon={LuUserCheck} label="Active" value={active} accent="bg-teal/15 text-teal" />
          <StatTile icon={LuUserX} label="Expired" value={expired} accent="bg-danger/15 text-danger" />
          <StatTile icon={LuUserMinus} label="Deactivated" value={deactivated} accent="bg-info/15 text-info" />
        </div>
        <ApexChart
          type="donut"
          height={240}
          getOptions={() => getStatusDonutOptions(total)}
          series={[pending, active, expired, deactivated]}
        />
      </div>
    </div>
  )
}
