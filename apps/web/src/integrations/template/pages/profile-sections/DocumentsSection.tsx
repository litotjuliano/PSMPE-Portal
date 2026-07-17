import { useEffect, useRef, useState, type ChangeEvent } from 'react'
import { LuEye, LuTrash2, LuUpload } from 'react-icons/lu'
import { memberApi, type MemberCertificate } from '../../../../core/api/endpoints/memberApi'
import { uploadApi } from '../../../../core/api/endpoints/uploadApi'
import { StandardButton } from '../../components/shared/StandardButton'
import { FilePreviewModal } from '../../components/shared/FilePreviewModal'
import { describeError } from './shared'

// Matches the backend's MemberUploadService/MemberCertificateService caps.
const MaxImageBytes = 24 * 1024 * 1024
const MaxPdfBytes = 2 * 1024 * 1024

interface SingleUploadSlotProps {
  label: string
  hint: string
  hasFile: boolean
  uploading: boolean
  onUpload: (file: File) => void
  onView: () => void
}

function maxBytesFor(file: File): number {
  return file.name.toLowerCase().endsWith('.pdf') ? MaxPdfBytes : MaxImageBytes
}

const SingleUploadSlot = ({ label, hint, hasFile, uploading, onUpload, onView }: SingleUploadSlotProps) => {
  const inputRef = useRef<HTMLInputElement>(null)

  const handleSelected = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (file) onUpload(file)
  }

  return (
    <div>
      <span className="block font-medium text-default-900 text-sm mb-2">{label}</span>
      <div className="flex items-center gap-3 flex-wrap">
        {hasFile ? (
          <StandardButton variant="view" size="sm" icon={LuEye} onClick={onView}>
            View
          </StandardButton>
        ) : (
          <span className="text-sm text-default-500">Not uploaded yet.</span>
        )}
        <input ref={inputRef} type="file" accept=".jpg,.jpeg,.png,.pdf" className="hidden" onChange={handleSelected} />
        <StandardButton
          variant="secondary"
          size="sm"
          icon={LuUpload}
          onClick={() => inputRef.current?.click()}
          loading={uploading}
          loadingLabel="Uploading…"
        >
          {hasFile ? 'Replace' : 'Upload'}
        </StandardButton>
      </div>
      <p className="text-xs text-default-500 mt-1">{hint}</p>
    </div>
  )
}

export const DocumentsSection = () => {
  const [error, setError] = useState<string | null>(null)

  const [hasValidGovernmentId, setHasValidGovernmentId] = useState(false)
  const [hasFormalPhoto, setHasFormalPhoto] = useState(false)
  const [hasSignature, setHasSignature] = useState(false)
  const [uploadingValidGovernmentId, setUploadingValidGovernmentId] = useState(false)
  const [uploadingFormalPhoto, setUploadingFormalPhoto] = useState(false)
  const [uploadingSignature, setUploadingSignature] = useState(false)
  const [previewOpen, setPreviewOpen] = useState<'validGovernmentId' | 'formalPhoto' | 'signature' | null>(null)

  const [certificates, setCertificates] = useState<MemberCertificate[]>([])
  const [loadingCertificates, setLoadingCertificates] = useState(true)
  const [uploadingCertificate, setUploadingCertificate] = useState(false)
  const [previewCertificateId, setPreviewCertificateId] = useState<string | null>(null)
  const certificateInputRef = useRef<HTMLInputElement>(null)

  const loadCertificates = () =>
    memberApi
      .getMyCertificates()
      .then(setCertificates)
      .catch(() => setCertificates([]))
      .finally(() => setLoadingCertificates(false))

  useEffect(() => {
    let cancelled = false
    uploadApi.fetchMyValidGovernmentIdUrl().then((result) => {
      if (!cancelled && result) {
        setHasValidGovernmentId(true)
        URL.revokeObjectURL(result.url)
      }
    })
    uploadApi.fetchMyFormalPhotoUrl().then((result) => {
      if (!cancelled && result) {
        setHasFormalPhoto(true)
        URL.revokeObjectURL(result.url)
      }
    })
    uploadApi.fetchMySignatureUrl().then((result) => {
      if (!cancelled && result) {
        setHasSignature(true)
        URL.revokeObjectURL(result.url)
      }
    })
    loadCertificates()
    return () => {
      cancelled = true
    }
  }, [])

  const handleUpload = async (
    file: File,
    upload: (file: File) => Promise<void>,
    setUploading: (value: boolean) => void,
    setHasFile: (value: boolean) => void,
  ) => {
    setError(null)
    if (file.size > maxBytesFor(file)) {
      setError('That file is too large. Images must be under 24 MB and PDFs under 2 MB.')
      return
    }
    setUploading(true)
    try {
      await upload(file)
      setHasFile(true)
    } catch (err) {
      setError(describeError(err, 'Could not upload this file. Make sure it is a JPG, PNG, or PDF under the size limit.'))
    } finally {
      setUploading(false)
    }
  }

  const handleCertificateSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return
    setError(null)
    if (file.size > maxBytesFor(file)) {
      setError('That file is too large. Images must be under 24 MB and PDFs under 2 MB.')
      return
    }
    setUploadingCertificate(true)
    try {
      await uploadApi.uploadMyCertificate(file)
      await loadCertificates()
    } catch (err) {
      setError(describeError(err, 'Could not upload this certificate. Make sure it is a JPG, PNG, or PDF under the size limit.'))
    } finally {
      setUploadingCertificate(false)
    }
  }

  const handleDeleteCertificate = async (certificateId: string) => {
    setError(null)
    try {
      await memberApi.deleteMyCertificate(certificateId)
      await loadCertificates()
    } catch (err) {
      setError(describeError(err, 'Could not delete this certificate. Please try again.'))
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <h6 className="font-semibold text-default-800">Documents</h6>
      <p className="text-sm text-default-500">
        Optional - these are ID-issuance requirements, upload them whenever you're ready.
      </p>

      {error && <p className="text-sm text-danger">{error}</p>}

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <SingleUploadSlot
          label="Valid Government ID"
          hint="JPG, PNG, or PDF under the size limit."
          hasFile={hasValidGovernmentId}
          uploading={uploadingValidGovernmentId}
          onUpload={(file) =>
            handleUpload(file, uploadApi.uploadMyValidGovernmentId, setUploadingValidGovernmentId, setHasValidGovernmentId)
          }
          onView={() => setPreviewOpen('validGovernmentId')}
        />
        <SingleUploadSlot
          label="2x2 Formal Photo"
          hint="Square, ~2x2 - JPG or PNG."
          hasFile={hasFormalPhoto}
          uploading={uploadingFormalPhoto}
          onUpload={(file) => handleUpload(file, uploadApi.uploadMyFormalPhoto, setUploadingFormalPhoto, setHasFormalPhoto)}
          onView={() => setPreviewOpen('formalPhoto')}
        />
        <SingleUploadSlot
          label="Signature"
          hint="JPG or PNG of your signature."
          hasFile={hasSignature}
          uploading={uploadingSignature}
          onUpload={(file) => handleUpload(file, uploadApi.uploadMySignature, setUploadingSignature, setHasSignature)}
          onView={() => setPreviewOpen('signature')}
        />
      </div>

      <div className="border-t border-default-200 pt-4">
        <div className="flex items-center justify-between mb-3">
          <span className="block font-medium text-default-900 text-sm">Certificates</span>
          <input ref={certificateInputRef} type="file" accept=".jpg,.jpeg,.png,.pdf" className="hidden" onChange={handleCertificateSelected} />
          <StandardButton
            variant="secondary"
            size="sm"
            icon={LuUpload}
            onClick={() => certificateInputRef.current?.click()}
            loading={uploadingCertificate}
            loadingLabel="Uploading…"
          >
            Add Certificate
          </StandardButton>
        </div>
        {loadingCertificates ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : certificates.length === 0 ? (
          <p className="text-sm text-default-500">No certificates uploaded yet.</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {certificates.map((cert) => (
              <li key={cert.id} className="flex items-center justify-between gap-3 text-sm border border-default-200 rounded-lg px-3 py-2">
                <span className="truncate">{cert.fileName}</span>
                <div className="flex items-center gap-2 shrink-0">
                  <StandardButton variant="view" size="sm" icon={LuEye} onClick={() => setPreviewCertificateId(cert.id)}>
                    View
                  </StandardButton>
                  <StandardButton variant="danger" size="sm" icon={LuTrash2} onClick={() => handleDeleteCertificate(cert.id)}>
                    Delete
                  </StandardButton>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      <FilePreviewModal
        isOpen={previewOpen === 'validGovernmentId'}
        title="Valid Government ID"
        fetchFile={() => uploadApi.fetchMyValidGovernmentIdUrl()}
        onClose={() => setPreviewOpen(null)}
      />
      <FilePreviewModal
        isOpen={previewOpen === 'formalPhoto'}
        title="2x2 Formal Photo"
        fetchFile={() => uploadApi.fetchMyFormalPhotoUrl()}
        onClose={() => setPreviewOpen(null)}
      />
      <FilePreviewModal
        isOpen={previewOpen === 'signature'}
        title="Signature"
        fetchFile={() => uploadApi.fetchMySignatureUrl()}
        onClose={() => setPreviewOpen(null)}
      />
      <FilePreviewModal
        isOpen={previewCertificateId !== null}
        title="Certificate"
        fetchFile={() => uploadApi.fetchMyCertificateUrl(previewCertificateId!)}
        onClose={() => setPreviewCertificateId(null)}
      />
    </div>
  )
}
