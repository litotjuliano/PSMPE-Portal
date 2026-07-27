import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { LuBadgeCheck, LuTriangleAlert } from 'react-icons/lu'
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
import { FilePreviewModal } from '../components/shared/FilePreviewModal'
import { memberApi, type ProfileCompleteness } from '../../../core/api/endpoints/memberApi'
import { uploadApi } from '../../../core/api/endpoints/uploadApi'
import { useAuth } from '../../../core/auth/useAuth'
import { Roles } from '../../../core/types/auth'

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
 * The in-dashboard half of the approval notification (see MembersController.Approve /
 * IssueApprovalReceiptAsync for the email half) - only ever renders once the receipt file
 * actually exists, i.e. once an admin has approved the application, so there's nothing to gate on
 * beyond the fetch itself succeeding.
 */
function ReceiptBanner() {
  const [receiptAvailable, setReceiptAvailable] = useState(false)
  const [previewOpen, setPreviewOpen] = useState(false)

  useEffect(() => {
    uploadApi.fetchMyReceiptUrl().then((result) => {
      if (result) {
        setReceiptAvailable(true)
        URL.revokeObjectURL(result.url) // only needed the existence check here, not the bytes
      }
    })
  }, [])

  if (!receiptAvailable) {
    return null
  }

  const handleDownload = async () => {
    const result = await uploadApi.fetchMyReceiptUrl()
    if (!result) return
    const link = document.createElement('a')
    link.href = result.url
    link.download = 'PSMPE-Receipt.jpg'
    link.click()
    URL.revokeObjectURL(result.url)
  }

  return (
    <div className="card mb-5 border border-success/30 bg-success/10">
      <div className="card-body flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <LuBadgeCheck className="size-5 text-success shrink-0" />
          <div>
            <h6 className="font-semibold text-default-900">Your membership has been approved</h6>
            <p className="text-sm text-default-600">Your official receipt is ready to view or download.</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => setPreviewOpen(true)}
            className="btn btn-sm border border-default-200 whitespace-nowrap"
          >
            View Receipt
          </button>
          <button type="button" onClick={handleDownload} className="btn btn-sm bg-success text-white whitespace-nowrap">
            Download Receipt
          </button>
        </div>
      </div>
      <FilePreviewModal
        isOpen={previewOpen}
        title="Receipt"
        fetchFile={() => uploadApi.fetchMyReceiptUrl()}
        onClose={() => setPreviewOpen(false)}
      />
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
  const { user } = useAuth()
  const isMember = user?.roles.includes(Roles.Member) ?? false

  return (
    <>
      <PageMeta title="Dashboard" />
      <main>
        <PageBreadcrumb title="Dashboard" />
        <CompleteApplicationBanner />
        {isMember && <ReceiptBanner />}
        <ProfileCompletenessGauge />
        {!isMember && (
          <>
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
          </>
        )}
      </main>
    </>
  )
}
