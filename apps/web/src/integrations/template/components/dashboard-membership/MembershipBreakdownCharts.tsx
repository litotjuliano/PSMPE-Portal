import type { ApexOptions } from 'apexcharts'
import ApexChart from '../shared/ApexChart'
import type { MemberStats } from '../../../../core/api/endpoints/memberApi'

type NamedCounts = MemberStats['byChapter']

const getBreakdownChartOptions = (categories: string[], color: string): ApexOptions => ({
  chart: { type: 'bar', toolbar: { show: false } },
  plotOptions: { bar: { borderRadius: 4, horizontal: true, barHeight: '55%' } },
  dataLabels: { enabled: false },
  colors: [color],
  xaxis: { categories },
  grid: {
    padding: { top: -20, right: 0 },
  },
})

function BreakdownPanel({ title, rows, color }: { title: string; rows: NamedCounts; color: string }) {
  const categories = rows.map((r) => r.name)
  const data = rows.map((r) => r.count)

  return (
    <div className="card">
      <div className="card-header">
        <h6 className="card-title">{title}</h6>
      </div>
      <div className="card-body">
        <ApexChart
          type="bar"
          height={260}
          getOptions={() => getBreakdownChartOptions(categories, color)}
          series={[{ name: 'Members', data }]}
        />
      </div>
    </div>
  )
}

/** Two side-by-side panels for the chapter and member-type splits - both are single-series,
 *  horizontal bar charts (labels can be long, e.g. "Quezon City" / "Senior Citizen"), each with
 *  its own single accent hue since color here encodes magnitude, not category identity. */
export function MembershipBreakdownCharts({ byChapter, byMemberType }: { byChapter: NamedCounts; byMemberType: NamedCounts }) {
  return (
    <div className="grid lg:grid-cols-2 grid-cols-1 gap-5">
      <BreakdownPanel title="Members by Chapter" rows={byChapter} color="#0E4C92" />
      <BreakdownPanel title="Members by Type" rows={byMemberType} color="#0F9BA8" />
    </div>
  )
}
