import { useEffect, useRef, useState, type ChangeEvent } from 'react'
import { LuEye, LuSquarePen, LuTriangleAlert, LuUpload } from 'react-icons/lu'
import type { Member } from '../../../../core/types/member'
import { memberApi } from '../../../../core/api/endpoints/memberApi'
import { uploadApi } from '../../../../core/api/endpoints/uploadApi'
import { StandardButton } from '../../components/shared/StandardButton'
import { FilePreviewModal } from '../../components/shared/FilePreviewModal'
import { buildFullProfileRequest, describeError } from './shared'

// Matches the backend's MemberUploadService caps - see MembershipApplicationWizardCard.tsx.
const MaxImageBytes = 24 * 1024 * 1024
const MaxPdfBytes = 2 * 1024 * 1024

interface PrcInformationSectionProps {
  member: Member
  onUpdated: (member: Member) => void
}

interface FormState {
  prcLicenseNo: string
  ptrNumber: string
  tin: string
}

function toFormState(member: Member): FormState {
  return {
    prcLicenseNo: member.prcLicenseNo ?? '',
    ptrNumber: member.ptrNumber ?? '',
    tin: member.tin ?? '',
  }
}

/** Fields required to submit a new application - existing members are only ever nudged to
 *  complete these, never blocked from saving unrelated changes (see MemberService.SubmitMyProfileAsync). */
function missingRequiredFields(member: Member): string[] {
  const missing: string[] = []
  if (!member.prcLicenseNo) missing.push('PRC License No.')
  if (!member.ptrNumber) missing.push('PTR Number')
  return missing
}

export const PrcInformationSection = ({ member, onUpdated }: PrcInformationSectionProps) => {
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState<FormState>(() => toFormState(member))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const prcIdInputRef = useRef<HTMLInputElement>(null)
  const [uploadingPrcId, setUploadingPrcId] = useState(false)
  const [hasPrcId, setHasPrcId] = useState(false)
  const [prcIdPreviewOpen, setPrcIdPreviewOpen] = useState(false)
  // Tracks whether the PRC ID was re-uploaded during *this* Edit Mode session - reset whenever
  // Edit Mode is (re-)entered, since a change made in an earlier session doesn't count.
  const [prcIdJustReuploaded, setPrcIdJustReuploaded] = useState(false)

  useEffect(() => {
    let cancelled = false
    uploadApi.fetchMyPrcIdUrl().then((result) => {
      if (!cancelled && result) {
        setHasPrcId(true)
        URL.revokeObjectURL(result.url)
      }
    })
    return () => {
      cancelled = true
    }
  }, [])

  const startEditing = () => {
    setForm(toFormState(member))
    setPrcIdJustReuploaded(false)
    setError(null)
    setEditing(true)
  }

  const cancelEditing = () => {
    setForm(toFormState(member))
    setError(null)
    setEditing(false)
  }

  const handleChange = <K extends keyof FormState>(field: K, value: FormState[K]) => {
    setForm((current) => ({ ...current, [field]: value }))
  }

  const handlePrcIdSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    if (!file) return
    setError(null)
    const isPdf = file.name.toLowerCase().endsWith('.pdf')
    const maxBytes = isPdf ? MaxPdfBytes : MaxImageBytes
    if (file.size > maxBytes) {
      setError(
        isPdf ? 'That PDF is too large (max 2 MB). Please choose a smaller file.' : 'That file is too large (max 24 MB). Please choose a smaller file.',
      )
      event.target.value = ''
      return
    }

    setUploadingPrcId(true)
    try {
      await uploadApi.uploadMyPrcId(file)
      setHasPrcId(true)
      setPrcIdJustReuploaded(true)
    } catch (err) {
      setError(describeError(err, 'Could not upload PRC ID. Make sure it is a JPG, PNG, or PDF under the size limit.'))
    } finally {
      setUploadingPrcId(false)
    }
  }

  const prcLicenseNoChanged = form.prcLicenseNo !== (member.prcLicenseNo ?? '')
  const blockedByMissingReupload = prcLicenseNoChanged && !prcIdJustReuploaded

  const handleSave = async () => {
    setError(null)
    if (blockedByMissingReupload) {
      setError('Upload a new PRC ID document to save this change to PRC License No.')
      return
    }
    if (form.tin && !/^[\d-]{9,12}$/.test(form.tin)) {
      setError('TIN must be 9-12 digits, with dashes allowed as separators.')
      return
    }

    setSaving(true)
    try {
      const updated = await memberApi.updateMyProfile(
        buildFullProfileRequest(member, {
          prcLicenseNo: form.prcLicenseNo || null,
          ptrNumber: form.ptrNumber || null,
          tin: form.tin || null,
          prcIdReuploaded: prcLicenseNoChanged && prcIdJustReuploaded,
        }),
      )
      onUpdated(updated)
      setEditing(false)
    } catch (err) {
      setError(describeError(err, 'Could not save your changes. Please try again.'))
    } finally {
      setSaving(false)
    }
  }

  const missing = missingRequiredFields(member)

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h6 className="font-semibold text-default-800">PRC Information</h6>
        {!editing && (
          <StandardButton size="sm" icon={LuSquarePen} onClick={startEditing}>
            Edit
          </StandardButton>
        )}
      </div>

      {error && <p className="text-sm text-danger">{error}</p>}

      {!editing && missing.length > 0 && (
        <p className="text-sm text-warning bg-warning/10 rounded-lg px-3 py-2 flex items-start gap-2">
          <LuTriangleAlert className="size-4 shrink-0 mt-0.5" />
          Please complete the following: {missing.join(', ')}.
        </p>
      )}

      {member.prcVerificationRejectedReason && (
        <p className="text-sm text-danger bg-danger/10 rounded-lg px-3 py-2">
          Your requested PRC License No. change was not approved: {member.prcVerificationRejectedReason}
        </p>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-sm">
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">PRC License No.</span>
          {editing ? (
            <>
              <input className="form-input" value={form.prcLicenseNo} onChange={(e) => handleChange('prcLicenseNo', e.target.value)} />
              {prcLicenseNoChanged && (
                <p className="text-xs text-warning mt-1">
                  {prcIdJustReuploaded ? 'New PRC ID uploaded - ready to save.' : 'Upload a new PRC ID document below to save this change.'}
                </p>
              )}
            </>
          ) : (
            <>
              <span className="font-semibold text-default-800">{member.prcLicenseNo || '-'}</span>
              {member.pendingPrcLicenseNo ? (
                <p className="text-xs text-warning mt-1">New value "{member.pendingPrcLicenseNo}" - pending admin verification.</p>
              ) : (
                !member.prcIdVerified &&
                member.prcLicenseNo && <p className="text-xs text-warning mt-1">Pending admin verification.</p>
              )}
            </>
          )}
        </div>
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">PTR Number</span>
          {editing ? (
            <input className="form-input" value={form.ptrNumber} onChange={(e) => handleChange('ptrNumber', e.target.value)} />
          ) : (
            <span className="font-semibold text-default-800">{member.ptrNumber || '-'}</span>
          )}
        </div>
        <div>
          <span className="block font-medium text-default-900 text-sm mb-2">TIN</span>
          {editing ? (
            <input
              className="form-input"
              placeholder="000-000-000-000"
              value={form.tin}
              onChange={(e) => handleChange('tin', e.target.value)}
            />
          ) : (
            <span className="font-semibold text-default-800">{member.tin || '-'}</span>
          )}
        </div>

        <div className="md:col-span-2">
          <span className="block font-medium text-default-900 text-sm mb-2">PRC ID Document</span>
          <div className="flex items-center gap-3">
            {hasPrcId ? (
              <StandardButton variant="view" icon={LuEye} onClick={() => setPrcIdPreviewOpen(true)}>
                View PRC ID
              </StandardButton>
            ) : (
              <span className="text-default-500">No PRC ID uploaded yet.</span>
            )}
            {editing && (
              <>
                <input ref={prcIdInputRef} type="file" accept=".jpg,.jpeg,.png,.pdf" className="hidden" onChange={handlePrcIdSelected} />
                <StandardButton
                  variant="secondary"
                  icon={LuUpload}
                  onClick={() => prcIdInputRef.current?.click()}
                  loading={uploadingPrcId}
                  loadingLabel="Uploading…"
                >
                  {hasPrcId ? 'Replace file' : 'Upload'}
                </StandardButton>
              </>
            )}
          </div>
        </div>
      </div>

      {editing && (
        <div className="flex items-center gap-2">
          <StandardButton onClick={handleSave} disabled={blockedByMissingReupload} loading={saving} loadingLabel="Saving…">
            Save
          </StandardButton>
          <StandardButton variant="secondary" onClick={cancelEditing} disabled={saving}>
            Cancel
          </StandardButton>
        </div>
      )}

      <FilePreviewModal
        isOpen={prcIdPreviewOpen}
        title="PRC ID Document"
        fetchFile={() => uploadApi.fetchMyPrcIdUrl()}
        onClose={() => setPrcIdPreviewOpen(false)}
      />
    </div>
  )
}
