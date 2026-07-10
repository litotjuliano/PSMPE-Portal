import ApexChart from '../shared/ApexChart'
import Flatpickr from 'react-flatpickr'
import { LuCalendarRange, LuChartColumn, LuTrendingUp } from 'react-icons/lu'
import { getSalesRevenueOverview } from './data'

const SalesRevenueOverview = () => {
  return (
    <div className="lg:col-span-2 col-span-1">
      <div className="card">
        <div className="card-header">
          <h6 className="card-title">Sales Revenue Overview</h6>

          <div className="relative">
            <Flatpickr
              options={{
                mode: 'range',
                dateFormat: 'd M, Y',
              }}
              className="form-input form-input-sm ps-9 w-full"
              placeholder="Select Date"
            />
            <div className="absolute inset-y-0 start-0 flex items-center ps-3">
              <LuCalendarRange className="size-4 text-default-500" />
            </div>
          </div>
        </div>

        <div className="card-body">
          <div className="grid md:grid-cols-4 grid-cols-1 mb-3 gap-4">
            <div className="col-span-1">
              <div className="flex items-center gap-3">
                <div className="btn size-12 bg-secondary/10">
                  <LuChartColumn className="text-secondary size-6" />
                </div>

                <div>
                  <p className="mb-1.5 text-default-500 text-sm">Total Sales</p>
                  <h5 className="text-base font-semibold text-default-800">
                    $<span data-target="1517.36">1,517.36</span>k
                  </h5>
                </div>
              </div>
            </div>

            <div className="col-span-1">
              <div className="flex items-center gap-3">
                <div className="btn size-12 bg-success/10">
                  <LuTrendingUp className="text-success size-6" />
                </div>

                <div>
                  <p className="mb-1.5 text-default-500 text-sm">Total Profit</p>
                  <h5 className="text-base font-semibold text-default-800">
                    $<span data-target="746.84">746.84</span>k
                  </h5>
                </div>
              </div>
            </div>
          </div>
          <ApexChart getOptions={getSalesRevenueOverview} series={getSalesRevenueOverview().series} type="bar" height={310} />
        </div>
      </div>
    </div>
  )
}

export default SalesRevenueOverview
