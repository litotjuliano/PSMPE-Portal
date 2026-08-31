import { useRef, useState } from 'react'
import { LuEye, LuFile, LuUpload } from 'react-icons/lu'
import { describeError } from '../../../../core/utils/apiError'
import { FilePreviewModal } from './FilePreviewModal'
import { StandardButton } from './StandardButton'

interface FetchedFile {
  url: string
  contentType: string
}

interface ProofOfPaymentControlProps {
  /** Fetches the file for this payment. Must throw (not resolve null) when the file can't be
   *  found - see paymentApi.fetchProofUrl, which only resolves null for "nothing was ever
   *  submitted" (a case this control is never rendered for - callers only mount it when
   *  payment.hasProof is true). */
  fetchProof: () => Promise<FetchedFile>
  /** Uploads a replacement file for this same payment (re-attaching proof, not creating a new
   *  payment) - see paymentApi.uploadProof / eventApi.uploadPaymentProof, both of which hit the
   *  same POST /api/payments/{id}/proof endpoint. Return value ignored - callers pass their API
   *  method directly regardless of what it resolves to. */
  uploadProof: (file: File) => Promise<unknown>
  /** Called after a successful re-upload, so the caller can refresh its own payment list/state. */
  onUploaded?: () => void
  /** Whether re-attaching a replacement file makes sense for this payment right now - true only
   *  while it's still Submitted (pending admin review, where a missing file genuinely blocks
   *  verification). Once a payment is Verified there is nothing pending for a lost file to block,
   *  and a Rejected one is superseded by a brand new payment through the normal form, not a patch
   *  to the old row - so a missing file for either just gets an informational note, no "act on
   *  this" prompt aimed at the member. */
  allowResubmit: boolean
}

/**
 * "View proof" for a payment that has one recorded. If the file turns out to be missing from
 * storage (see PaymentsController.GetProof's 410 response), this does NOT pop the (otherwise
 * empty) Proof of Payment viewer - it shows the problem and a way to fix it right there instead:
 * an inline message plus an "Upload Receipts" control to re-attach proof to this same payment.
 */
export function ProofOfPaymentControl({ fetchProof, uploadProof, onUploaded, allowResubmit }: ProofOfPaymentControlProps) {
  const [status, setStatus] = useState<'idle' | 'checking' | 'missing' | 'uploading'>('idle')
  const [foundFile, setFoundFile] = useState<FetchedFile | null>(null)
  const [viewerOpen, setViewerOpen] = useState(false)
  const [uploadError, setUploadError] = useState<string | null>(null)
  const [selectedFileName, setSelectedFileName] = useState<string | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const handleViewProof = async () => {
    setStatus('checking')
    try {
      const file = await fetchProof()
      setFoundFile(file)
      setViewerOpen(true)
      setStatus('idle')
    } catch {
      setStatus('missing')
    }
  }

  const handleUpload = async () => {
    const file = fileInputRef.current?.files?.[0]
    if (!file) return
    setStatus('uploading')
    setUploadError(null)
    try {
      await uploadProof(file)
      if (fileInputRef.current) fileInputRef.current.value = ''
      setSelectedFileName(null)
      setStatus('idle')
      onUploaded?.()
    } catch (err) {
      setUploadError(describeError(err, 'Could not upload the file. Please try again.'))
      setStatus('missing')
    }
  }

  if ((status === 'missing' || status === 'uploading') && !allowResubmit) {
    // Verified/Rejected: nothing is pending on this payment, so there's no action for the member
    // to take - a lost archival file is a backend data-integrity note, not their problem to fix.
    return (
      <span className="text-xs text-default-500 max-w-sm block">
        Proof file unavailable (this payment has already been decided, so it can't be resubmitted).
      </span>
    )
  }

  if (status === 'missing' || status === 'uploading') {
    return (
      <div className="text-sm text-danger bg-danger/10 rounded-lg px-3 py-2 flex flex-col gap-2 max-w-sm">
        <span>
          This file was recorded but could not be found in storage. It may have been lost — please resubmit your
          proof.
        </span>
        {uploadError && <span className="font-medium">{uploadError}</span>}
        <input
          ref={fileInputRef}
          type="file"
          accept="image/jpeg,image/png,application/pdf"
          className="hidden"
          onChange={(e) => setSelectedFileName(e.target.files?.[0]?.name ?? null)}
        />
        <div className="flex items-center gap-2 flex-wrap">
          <StandardButton
            variant="primary"
            size="sm"
            icon={LuFile}
            onClick={() => fileInputRef.current?.click()}
            disabled={status === 'uploading'}
          >
            Choose File
          </StandardButton>
          <span className="text-xs text-default-500 truncate">{selectedFileName ?? 'No file chosen'}</span>
        </div>
        <StandardButton
          variant="danger"
          size="sm"
          icon={LuUpload}
          onClick={handleUpload}
          disabled={!selectedFileName}
          loading={status === 'uploading'}
          loadingLabel="Uploading…"
        >
          Upload Receipts
        </StandardButton>
      </div>
    )
  }

  return (
    <>
      <StandardButton
        variant="view"
        size="sm"
        icon={LuEye}
        onClick={handleViewProof}
        loading={status === 'checking'}
        loadingLabel="Checking…"
      >
        View proof
      </StandardButton>

      {viewerOpen && foundFile && (
        <FilePreviewModal
          isOpen
          title="Proof of Payment"
          fetchFile={() => Promise.resolve(foundFile)}
          onClose={() => setViewerOpen(false)}
        />
      )}
    </>
  )
}
