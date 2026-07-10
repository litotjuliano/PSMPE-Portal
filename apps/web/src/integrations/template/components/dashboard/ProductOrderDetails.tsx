import type { ElementType } from 'react'
import { LuPackage, LuPackageX, LuTruck, LuWallet } from 'react-icons/lu'

type OrderStat = {
  id: number
  icon: ElementType
  iconColor: string
  bgColor: string
  value: string
  prefix?: string
  suffix?: string
  label: string
}

const stats: OrderStat[] = [
  {
    id: 1,
    icon: LuWallet,
    iconColor: 'text-primary',
    bgColor: 'bg-primary/10',
    value: '236.18',
    prefix: '$',
    suffix: 'k',
    label: 'Total Revenue',
  },
  {
    id: 2,
    icon: LuPackage,
    iconColor: 'text-info',
    bgColor: 'bg-info/10',
    value: '13,461',
    label: 'Total Orders',
  },
  {
    id: 3,
    icon: LuTruck,
    iconColor: 'text-success',
    bgColor: 'bg-success/10',
    value: '17,150',
    label: 'Delivered',
  },
  {
    id: 4,
    icon: LuPackageX,
    iconColor: 'text-danger',
    bgColor: 'bg-danger/10',
    value: '3,519',
    label: 'Cancelled',
  },
]

const ProductOrderDetails = () => {
  return (
    <div className="grid lg:grid-cols-4 md:grid-cols-2 grid-cols-1 gap-5">
      {stats.map(({ id, icon: Icon, iconColor, bgColor, value, prefix, suffix, label }) => (
        <div className="card" key={id}>
          <div className="card-body">
            <div className={`flex items-center justify-center mx-auto rounded-full size-14 ${bgColor}`}>
              <Icon className={`size-6 ${iconColor}`} />
            </div>
            <h5 className="mt-4 text-center mb-2 text-default-800 font-semibold text-lg">
              {prefix}
              <span data-target={value}>{value}</span>
              {suffix}
            </h5>
            <p className="text-center text-sm text-default-500">{label}</p>
          </div>
        </div>
      ))}
    </div>
  )
}

export default ProductOrderDetails
