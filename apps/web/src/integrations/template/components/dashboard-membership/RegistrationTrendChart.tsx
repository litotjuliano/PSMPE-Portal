import type { ApexOptions } from 'apexcharts'
import ApexChart from '../shared/ApexChart'
import type { MemberStats } from '../../../../core/api/endpoints/memberApi'

type RegistrationTrend = MemberStats['registrationTrend']

const MONTH_LABELS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

const getTrendChartOptions = (categories: string[]): ApexOptions => ({
  chart: { type: 'bar', toolbar: { show: false } },
  plotOptions: { bar: { borderRadius: 4, columnWidth: '50%' } },
  dataLabels: { enabled: false },
  colors: ['#0E4C92'], // --color-primary
  xaxis: { categories },
  grid: {
    padding: { top: -20, right: 0 },
  },
})

/**
 * Last 12 calendar months of new registrations - `registrationTrend` is already zero-filled and
 * ordered oldest-first by the backend (see MemberStatsDto), so no client-side date bucketing.
 */
export function RegistrationTrendChart({ registrationTrend }: { registrationTrend: RegistrationTrend }) {
  const categories = registrationTrend.map((r) => `${MONTH_LABELS[r.month - 1]} '${String(r.year).slice(2)}`)
  const data = registrationTrend.map((r) => r.count)

  return (
    <div className="card">
      <div className="card-header">
        <h6 className="card-title">Registrations - Last 12 Months</h6>
      </div>
      <div className="card-body">
        <ApexChart
          type="bar"
          height={275}
          getOptions={() => getTrendChartOptions(categories)}
          series={[{ name: 'New Registrations', data }]}
        />
      </div>
    </div>
  )
}
