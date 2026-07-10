import ApexChart from '../shared/ApexChart'
import { LuMoveRight } from 'react-icons/lu'
import { Link } from 'react-router-dom'
import { getOrderStatisticsChart } from './data'

const OrderStatistics = () => {
  return (
    <div className="col-span-1">
      <div className="card">
        <div className="card-header">
          <h6 className="card-title">Order Statistics</h6>
          <Link to="#" className="btn btn-sm border-0 text-primary/90 hover:text-primary">
            View All
            <LuMoveRight className="ms-1 size-4" />
          </Link>
        </div>

        <div className="card-body">
          <ApexChart getOptions={getOrderStatisticsChart} series={getOrderStatisticsChart().series} type="line" height={275} />
        </div>
      </div>
    </div>
  )
}

export default OrderStatistics
