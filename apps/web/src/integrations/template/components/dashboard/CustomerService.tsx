import avatar2 from '../../assets/images/user/avatar-2.png'
import avatar3 from '../../assets/images/user/avatar-3.png'
import avatar4 from '../../assets/images/user/avatar-4.png'
import avatar5 from '../../assets/images/user/avatar-5.png'
import { Link } from 'react-router-dom'
import { LuCalendarDays, LuEllipsisVertical } from 'react-icons/lu'

type Customer = {
  id: number
  name: string
  email: string
  amount: string
  image: string
}

const customers: Customer[] = [
  { id: 1, name: 'Urrie Arthur', email: 'arthur@example.com', amount: '$2,452', image: avatar2 },
  { id: 2, name: 'Natalie Christy', email: 'natalie@example.com', amount: '$1,893', image: avatar3 },
  { id: 3, name: 'Laurie Jackson', email: 'jackson@example.com', amount: '$1,196', image: avatar4 },
  { id: 4, name: 'Michael Torres', email: 'torres@example.com', amount: '$976', image: avatar5 },
]
const dropdownOptions = ['1 Weekly', '1 Monthly', '3 Monthly', '6 Monthly', 'This Yearly']

const CustomerService = () => {
  return (
    <div className="card">
      <div className="card-header flex justify-between items-center">
        <h6 className="card-title">Customer Service</h6>

        <div className="hs-dropdown relative inline-flex">
          <button
            type="button"
            className="hs-dropdown-toggle btn size-7.5 text-sm font-semibold rounded-lg hover:bg-default-150"
            aria-haspopup="menu"
            aria-expanded="false"
            aria-label="Dropdown"
            hs-dropdown-placement="bottom-end"
          >
            <LuEllipsisVertical className="size-4 text-default-500" />
          </button>

          <div className="hs-dropdown-menu hidden absolute z-10 mt-2 w-40 bg-white border rounded-lg shadow-md" role="menu">
            {dropdownOptions.map((option, index) => (
              <Link key={index} className="flex items-center gap-x-3.5 py-1.5 font-medium px-3 text-default-600 hover:bg-default-150 rounded" to="#">
                {option}
              </Link>
            ))}
          </div>
        </div>
      </div>

      <div className="card-body">
        <div>
          <div className="flex items-center justify-between mb-2">
            <p className="text-default-500 text-sm">28% of the Goal Reached ($25k)</p>
          </div>

          <div className="w-full h-2 rounded-full bg-default-200">
            <div className="h-2 bg-success rounded-full" style={{ width: '28%' }}></div>
          </div>

          <div className="flex mt-3">
            <div className="flex items-center gap-2">
              <LuCalendarDays className="size-4 text-default-800" />
              <p className="mb-0 text-default-600">
                This Month: <span className="font-medium text-default-800">$13,741</span>
              </p>
            </div>
          </div>
        </div>

        <div>
          <h6 className="mt-4 mb-3 text-default-800 text-sm font-semibold">Top Customer</h6>
          <div className="divide-y divide-default-200">
            {customers.map((customer) => (
              <div key={customer.id} className="flex items-center justify-between text-sm py-2.25">
                <div className="flex items-center gap-3">
                  <div className="size-8 rounded-full bg-default-100 overflow-hidden">
                    <img src={customer.image} alt={customer.name} width={32} height={32} className="size-8 rounded-full" />
                  </div>
                  <div>
                    <h6 className="font-medium text-default-800 mb-1">{customer.name}</h6>
                    <p className="text-default-500 font-normal">{customer.email}</p>
                  </div>
                </div>
                <h6 className="font-semibold text-default-800">{customer.amount}</h6>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  )
}

export default CustomerService
