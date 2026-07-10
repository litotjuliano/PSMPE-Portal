import PageBreadcrumb from '../components/shared/PageBreadcrumb'
import PageMeta from '../components/shared/PageMeta'
import Audience from '../components/dashboard/Audience'
import CustomerService from '../components/dashboard/CustomerService'
import OrderStatistics from '../components/dashboard/OrderStatistics'
import ProductOrderDetails from '../components/dashboard/ProductOrderDetails'
import ProductOrders from '../components/dashboard/ProductOrders'
import SalesRevenueOverview from '../components/dashboard/SalesRevenueOverview'
import SalesThisMonth from '../components/dashboard/SalesThisMonth'
import TopSellingProducts from '../components/dashboard/TopSellingProducts'
import TrafficResources from '../components/dashboard/TrafficResources'
import WelcomeUser from '../components/dashboard/WelcomeUser'

export const DashboardPage = () => {
  return (
    <>
      <PageMeta title="Dashboard" />
      <main>
        <PageBreadcrumb title="Dashboard" />
        <div className="grid lg:grid-cols-3 grid-cols-1 gap-5 mb-5">
          <div className="lg:col-span-2 col-span-1">
            <WelcomeUser />
            <ProductOrderDetails />
          </div>
          <OrderStatistics />
        </div>
        <div className="grid lg:grid-cols-3 grid-cols-1 gap-5 mb-5">
          <SalesRevenueOverview />
          <TrafficResources />
        </div>
        <ProductOrders />
        <div className="grid lg:grid-cols-4 grid-cols-1 gap-5">
          <CustomerService />
          <SalesThisMonth />
          <TopSellingProducts />
          <Audience />
        </div>
      </main>
    </>
  )
}
