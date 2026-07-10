import { Link } from 'react-router-dom'
import { LuChevronLeft, LuChevronRight, LuDownload, LuEllipsis, LuEye, LuSearch, LuSquarePen, LuTrash2 } from 'react-icons/lu'

type Order = {
  id: number
  orderId: string
  customerName: string
  location: string
  orderDate: string
  payment: string
  quantity: number
  price: string
  totalAmount: string
  status: 'Delivered' | 'Shipping' | 'New' | 'Pending'
}

const orders: Order[] = [
  {
    id: 1,
    orderId: '#TWT5015100365',
    customerName: 'Marie Prohaska',
    location: 'Germany',
    orderDate: '08 Jun, 2023',
    payment: 'Credit Card',
    quantity: 5,
    price: '$146.99',
    totalAmount: '$749.95',
    status: 'Delivered',
  },
  {
    id: 2,
    orderId: '#TWT5015100366',
    customerName: 'Jaqueline Hammes',
    location: 'France',
    orderDate: '11 July, 2023',
    payment: 'Paypal',
    quantity: 2,
    price: '$450.00',
    totalAmount: '$900.00',
    status: 'Shipping',
  },
  {
    id: 3,
    orderId: '#TWT5015100367',
    customerName: 'Marlene Hirthe',
    location: 'Argentina',
    orderDate: '21 Aug, 2023',
    payment: 'Visa Card',
    quantity: 3,
    price: '$147.23',
    totalAmount: '$294.46',
    status: 'New',
  },
  {
    id: 4,
    orderId: '#TWT5015100368',
    customerName: 'Reagan Larson',
    location: 'Belgium',
    orderDate: '28 Nov, 2023',
    payment: 'American Express',
    quantity: 1,
    price: '$579.12',
    totalAmount: '$579.12',
    status: 'Delivered',
  },
  {
    id: 5,
    orderId: '#TWT5015100369',
    customerName: 'Glennie Langosh',
    location: 'Australia',
    orderDate: '11 Oct, 2023',
    payment: 'American Express',
    quantity: 1,
    price: '$349.00',
    totalAmount: '$349.00',
    status: 'Pending',
  },
  {
    id: 6,
    orderId: '#TWT5015100370',
    customerName: 'Rickie Cremin',
    location: 'United States',
    orderDate: '16 Feb, 2023',
    payment: 'COD',
    quantity: 3,
    price: '$89.49',
    totalAmount: '$268.47',
    status: 'Delivered',
  },
  {
    id: 7,
    orderId: '#TWT5015100371',
    customerName: 'Domenic Tromp',
    location: 'Afghanistan',
    orderDate: '21 Jan, 2024',
    payment: 'PayPal',
    quantity: 2,
    price: '$739.79',
    totalAmount: '$1,479.58',
    status: 'New',
  },
]

const statusClasses: Record<Order['status'], string> = {
  Delivered: 'bg-success/10 text-success border border-success/30',
  Shipping: 'bg-info/10 text-info border border-info/30',
  New: 'bg-secondary/10 text-secondary border border-secondary/30',
  Pending: 'bg-warning/10 text-warning border border-warning/30',
}

const ProductOrders = () => {
  return (
    <div className="grid grid-cols-1 gap-5 mb-5">
      <div className="card">
        <div className="card-header flex justify-between items-center">
          <h6 className="card-title">Product Orders</h6>

          <div className="flex gap-3 items-center">
            <div className="relative">
              <input type="text" className="form-input form-input-sm ps-9" placeholder="Search for..." />
              <div className="absolute inset-y-0 start-0 flex items-center ps-3">
                <LuSearch className="size-3.5 text-default-500" />
              </div>
            </div>

            <button className="btn btn-sm bg-primary text-white">
              <LuDownload className="size-3.5 me-1" />
              Export
            </button>
          </div>
        </div>

        <div className="flex flex-col">
          <div className="overflow-x-auto">
            <div className="min-w-full inline-block align-middle">
              <div className="overflow-hidden">
                <table className="min-w-full divide-y divide-default-200">
                  <thead className="bg-default-150">
                    <tr className="text-sm font-normal text-default-500 whitespace-nowrap">
                      <th className="px-3.5 py-3 text-start">#</th>
                      <th className="px-3.5 py-3 text-start">Order ID</th>
                      <th className="px-3.5 py-3 text-start">Customer Name</th>
                      <th className="px-3.5 py-3 text-start">Location</th>
                      <th className="px-3.5 py-3 text-start">Order Date</th>
                      <th className="px-3.5 py-3 text-start">Payments</th>
                      <th className="px-3.5 py-3 text-start">Quantity</th>
                      <th className="px-3.5 py-3 text-start">Price</th>
                      <th className="px-3.5 py-3 text-start">Total Amount</th>
                      <th className="px-3.5 py-3 text-start">Status</th>
                      <th className="px-3.5 py-3 text-start">Action</th>
                    </tr>
                  </thead>

                  <tbody className="divide-y divide-default-200">
                    {orders.map((order) => (
                      <tr key={order.id} className="text-default-800 font-normal">
                        <td className="px-3.5 py-2.5 text-sm">{order.id}</td>
                        <td className="px-3.5 py-2.5 text-sm">{order.orderId}</td>
                        <td className="px-3.5 py-2.5 text-sm">{order.customerName}</td>
                        <td className="px-3.5 py-2.5 text-sm">{order.location}</td>
                        <td className="px-3.5 py-2.5 text-sm">{order.orderDate}</td>
                        <td className="px-3.5 py-2.5 text-sm">{order.payment}</td>
                        <td className="px-3.5 py-2.5 text-sm">{order.quantity}</td>
                        <td className="px-3.5 py-2.5 text-sm">{order.price}</td>
                        <td className="px-3.5 py-2.5 text-sm">{order.totalAmount}</td>
                        <td className="px-3.5 py-2.5">
                          <span className={`inline-flex items-center gap-x-1.5 py-0.5 px-2.5 rounded text-xs font-medium ${statusClasses[order.status]}`}>
                            {order.status}
                          </span>
                        </td>
                        <td className="px-3.5 py-2.5">
                          <div className="hs-dropdown relative inline-flex">
                            <button
                              type="button"
                              className="hs-dropdown-toggle btn size-7.5 bg-default-150 hover:bg-default-600 text-default-500 hover:text-white"
                              aria-haspopup="menu"
                              aria-expanded="false"
                              aria-label="Dropdown"
                              hs-dropdown-placement="bottom-end"
                            >
                              <LuEllipsis className="iconify size-4" />
                            </button>

                            <div className="hs-dropdown-menu hidden" role="menu">
                              <div>
                                <Link className="flex items-center gap-1.5 py-1.5 font-medium px-3 text-sm text-default-500 hover:bg-default-150 rounded" to="#">
                                  <LuEye className="size-3" />
                                  Overview
                                </Link>
                                <Link className="flex items-center gap-1.5 py-1.5 font-medium px-3 text-sm text-default-500 hover:bg-default-150 rounded" to="#">
                                  <LuSquarePen className="size-3" />
                                  Edit
                                </Link>
                                <Link className="flex items-center gap-1.5 py-1.5 font-medium px-3 text-sm text-default-500 hover:bg-default-150 rounded" to="#">
                                  <LuTrash2 className="size-3" />
                                  Delete
                                </Link>
                              </div>
                            </div>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        </div>

        <div className="card-footer">
          <p className="text-default-500 text-sm">
            Showing <b>07</b> of <b>19</b> Results
          </p>
          <nav className="flex items-center gap-2" aria-label="Pagination">
            <button type="button" className="btn btn-sm border bg-transparent border-default-200 text-default-600 hover:bg-primary/10 hover:text-primary hover:border-primary/10">
              <LuChevronLeft className="size-4 me-1" /> Prev
            </button>

            <button type="button" className="btn size-7.5 bg-transparent border border-default-200 text-default-600 hover:bg-primary/10 hover:text-primary hover:border-primary/10">
              1
            </button>

            <button type="button" className="btn size-7.5 bg-primary text-white">
              2
            </button>

            <button type="button" className="btn size-7.5 bg-transparent border border-default-200 text-default-600 hover:bg-primary/10 hover:text-primary hover:border-primary/10">
              3
            </button>

            <button type="button" className="btn btn-sm border bg-transparent border-default-200 text-default-600 hover:bg-primary/10 hover:text-primary hover:border-primary/10">
              Next
              <LuChevronRight className="size-4 ms-1" />
            </button>
          </nav>
        </div>
      </div>
    </div>
  )
}

export default ProductOrders
