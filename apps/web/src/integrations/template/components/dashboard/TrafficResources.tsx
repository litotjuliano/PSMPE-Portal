import ApexChart from '../shared/ApexChart'
import { geTtrafficResourcesChart } from './data'
import { LuMoveRight, LuTrendingDown } from 'react-icons/lu'
import { Link } from 'react-router-dom'

const TrafficResources = () => {
  return (
    <div className="col-span-1">
      <div className="card mb-5 ">
        <div className="card-header">
          <h6 className="card-title">Traffic Resources</h6>
          <Link to="#" className="btn btn-sm border-0 text-primary/90 hover:text-primary">
            View Status
            <LuMoveRight className="ms-1 size-4" />
          </Link>
        </div>

        <div className="card-body">
          <div className="grid md:grid-cols-12 grid-cols-1">
            <div className="rounded-md md:col-span-7 col-span-1">
              <div id="trafficResourcesChart" dir="rtl"></div>
              <ApexChart getOptions={geTtrafficResourcesChart} series={geTtrafficResourcesChart().series} type="radialBar" height={222} />
            </div>

            <div className="md:col-span-5 col-span-1">
              <div className="flex flex-col gap-3">
                <div className="flex items-center gap-2">
                  <div className="bg-green-500 size-3" style={{ clipPath: 'polygon(50% 0%, 0% 100%, 100% 100%)' }}></div>
                  <p className="text-green-500">Search Engine (22%)</p>
                </div>

                <div className="flex items-center gap-2">
                  <div className="bg-purple-500 size-3" style={{ clipPath: 'polygon(50% 0%, 0% 100%, 100% 100%)' }}></div>
                  <p className="text-purple-500">Referral (34%)</p>
                </div>

                <div className="flex items-center gap-2">
                  <div className="bg-sky-500 size-3" style={{ clipPath: 'polygon(50% 0%, 0% 100%, 100% 100%)' }}></div>
                  <p className="text-sky-500">Direct (44%)</p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div className="card">
        <div className="card-body">
          <div className="flex items-center justify-between mb-2">
            <h5 className="text-lg text-default-900 font-semibold">
              <span data-target="1596">1,596</span>
            </h5>
            <span className="px-2.5 py-0.5 text-xs rounded border bg-transparent border-danger/50 text-danger flex items-center gap-1">
              <LuTrendingDown className="size-3" />
              6.8%
            </span>
          </div>

          <h6 className="font-semibold text-default-900">Monthly Orders Goal (20000+)</h6>

          <div>
            <div className="flex items-center justify-between mt-5 mb-2">
              <p className="text-default-500 text-sm">Total Orders</p>
              <h6 className="mb-0 text-primary">85%</h6>
            </div>
            <div className="w-full bg-default-200 rounded-full h-2.54">
              <div className="bg-primary h-2.5 rounded-full" style={{ width: '85%' }}></div>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

export default TrafficResources
