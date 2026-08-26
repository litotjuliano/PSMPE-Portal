import { useRef, useState, type ChangeEvent } from 'react'
import { LuPencil, LuUserRound } from 'react-icons/lu'
import { uploadApi } from '../../../../core/api/endpoints/uploadApi'
import { MAX_IMAGE_BYTES } from '../../../../core/constants/uploadLimits'
import { describeError } from '../../../../core/utils/apiError'
import type { Member } from '../../../../core/types/member'

interface ProfileRailProps {
  member: Member
  photoUrl: string | null
  onPhotoChanged: (url: string | null) => void
}

/**
 * Persistent left column for the Member Profile Summary card - photo and Membership ID, visible
 * across all five tabs rather than tied to whichever tab happens to be active (the identity data
 * used to live inside Personal Information alone, so switching tabs hid it).
 *
 * Upload is always available here (the pencil affordance), not gated by any tab's own edit state:
 * no single tab "owns" the photo any more, and this matches the immediate-upload pattern the
 * Documents & Certificates slots already use - the photo has always uploaded on selection,
 * independent of any section's Save button.
 */
export const ProfileRail = ({ member, photoUrl, onPhotoChanged }: ProfileRailProps) => {
  const photoInputRef = useRef<HTMLInputElement>(null)
  const [uploadingPhoto, setUploadingPhoto] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handlePhotoSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    if (!file) return
    setError(null)
    if (file.size > MAX_IMAGE_BYTES) {
      setError('That photo is too large (max 24 MB). Please choose a smaller file.')
      event.target.value = ''
      return
    }

    // Optimistic local preview so the rail updates the moment a file is picked; the page owns the
    // URL and revokes the one it replaces.
    onPhotoChanged(URL.createObjectURL(file))

    setUploadingPhoto(true)
    try {
      await uploadApi.uploadMyPhoto(file)
    } catch (err) {
      setError(describeError(err, 'Could not upload photo. Make sure it is a JPG or PNG under 24 MB.'))
    } finally {
      setUploadingPhoto(false)
    }
  }

  return (
    <div className="flex flex-row md:flex-col items-center md:items-stretch gap-4 md:gap-3 md:w-32 shrink-0 md:border-e md:border-default-200 md:pe-5">
      <div className="relative shrink-0 md:mx-auto">
        <div className="size-16 md:size-24 rounded-2xl bg-default-150 flex items-center justify-center overflow-hidden">
          {photoUrl ? (
            <img src={photoUrl} alt="Profile" className="size-full object-cover" />
          ) : (
            <LuUserRound className="size-8 md:size-12 text-default-400" />
          )}
        </div>
        <input ref={photoInputRef} type="file" accept=".jpg,.jpeg,.png" className="hidden" onChange={handlePhotoSelected} />
        <button
          type="button"
          onClick={() => photoInputRef.current?.click()}
          disabled={uploadingPhoto}
          title="Change photo"
          className="absolute -bottom-1 -end-1 flex items-center justify-center size-6 rounded-full bg-primary text-white shadow-sm hover:bg-primary/90 disabled:opacity-60"
        >
          <LuPencil className="size-3" />
        </button>
      </div>

      <div className="min-w-0">
        <span className="block text-xs text-default-500 mb-1">Membership ID</span>
        <span className={member.membershipNo ? 'font-semibold text-default-800 break-words block' : 'text-default-500 text-sm block'}>
          {member.membershipNo ?? 'Not yet assigned'}
        </span>
        {uploadingPhoto && <p className="text-xs text-default-500 mt-1">Uploading…</p>}
        {error && <p className="text-xs text-danger mt-1">{error}</p>}
      </div>
    </div>
  )
}
