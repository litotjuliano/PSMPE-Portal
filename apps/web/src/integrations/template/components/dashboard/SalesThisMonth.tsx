import ApexChart from '../shared/ApexChart'
import { Link } from 'react-router-dom'
import { LuEllipsisVertical, LuTrendingUp } from 'react-icons/lu'
import { getSalesThisMonthChart } from './data'

const SalesThisMonth = () => {
  return (
    <div className="card">
      <div className="card-header">
        <h6 className="card-title">Sales This Month</h6>

        <div className="hs-dropdown relative inline-flex">
          <button
            type="button"
            className="hs-dropdown-toggle btn  size-7.5 text-sm font-semibold rounded-lg hover:bg-default-150"
            aria-haspopup="menu"
            aria-expanded="false"
            aria-label="Dropdown"
            hs-dropdown-placement="bottom-end"
          >
            <LuEllipsisVertical className="iconify size-4 text-default-500" />
          </button>

          <div className="hs-dropdown-menu" role="menu">
            <Link className="flex items-center gap-x-3.5 py-1.5 font-medium px-3 text-default-600 hover:bg-default-150 rounded" to="#">
              1 Weekly
            </Link>
            <Link className="flex items-center gap-x-3.5 py-1.5 font-medium px-3 text-default-600 hover:bg-default-150 rounded" to="#">
              1 Monthly
            </Link>
            <Link className="flex items-center gap-x-3.5 py-1.5 font-medium px-3 text-default-600 hover:bg-default-150 rounded" to="#">
              3 Monthly
            </Link>
            <Link className="flex items-center gap-x-3.5 py-1.5 font-medium px-3 text-default-600 hover:bg-default-150 rounded" to="#">
              6 Monthly
            </Link>
            <Link className="flex items-center gap-x-3.5 py-1.5 font-medium px-3 text-default-600 hover:bg-default-150 rounded" to="#">
              This Yearly
            </Link>
          </div>
        </div>
      </div>

      <div className="card-body">
        <div className="flex items-center gap-3">
          <div className="flex items-center justify-center rounded-md size-12 bg-success/10">
            <LuTrendingUp className="size-6 text-success" />
          </div>
          <div className="text-sm">
            <p className="mb-1 text-default-500">Total Profit</p>
            <h5 className="font-semibold text-default-800">
              $<span data-target="746.84">746.84</span>k
            </h5>
          </div>
        </div>
        <ApexChart getOptions={getSalesThisMonthChart} series={getSalesThisMonthChart().series} type="rangeArea" height={280} />
      </div>
    </div>
  )
}

export default SalesThisMonth
