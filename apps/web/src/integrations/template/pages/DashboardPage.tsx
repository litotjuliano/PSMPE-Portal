import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { LuTriangleAlert } from 'react-icons/lu'
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
import { GaugeStat } from '../components/shared/GaugeStat'
import { memberApi, type ProfileCompleteness } from '../../../core/api/endpoints/memberApi'

function CompleteApplicationBanner() {
  const [needsCompletion, setNeedsCompletion] = useState(false)

  useEffect(() => {
    memberApi
      .getMyProfile()
      .then((member) => setNeedsCompletion(member.submittedAt === null))
      .catch(() => setNeedsCompletion(true))
  }, [])

  if (!needsCompletion) {
    return null
  }

  return (
    <div className="card mb-5 border border-warning/30 bg-warning/10">
      <div className="card-body flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <LuTriangleAlert className="size-5 text-warning shrink-0" />
          <div>
            <h6 className="font-semibold text-default-900">Complete your membership application</h6>
            <p className="text-sm text-default-600">
              Your account is ready, but your membership application isn't submitted yet - pick up where you left off.
            </p>
          </div>
        </div>
        <Link to="/profile" className="btn btn-sm bg-primary text-white whitespace-nowrap">
          Continue Application
        </Link>
      </div>
    </div>
  )
}

/**
 * Only meaningful once Steps 1-3 are submitted (CompleteApplicationBanner covers the "not
 * submitted yet" case above) - nudges toward the post-approval Professional Information/Documents
 * tabs in My Profile, which are otherwise easy to forget since nothing blocks approval on them.
 */
function ProfileCompletenessGauge() {
  const [completeness, setCompleteness] = useState<ProfileCompleteness | null>(null)

  useEffect(() => {
    memberApi
      .getMyProfileCompleteness()
      .then(setCompleteness)
      .catch(() => setCompleteness(null))
  }, [])

  if (!completeness || !completeness.isSubmitted || completeness.percentComplete >= 100) {
    return null
  }

  return (
    <div className="mb-5 max-w-sm">
      <GaugeStat
        label="Profile Completeness"
        value={completeness.percentComplete}
        helpText="Add your documents in My Profile to finish setting up your account."
      />
    </div>
  )
}

export const DashboardPage = () => {
  return (
    <>
      <PageMeta title="Dashboard" />
      <main>
        <PageBreadcrumb title="Dashboard" />
        <CompleteApplicationBanner />
        <ProfileCompletenessGauge />
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
