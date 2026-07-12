import { isAxiosError } from 'axios'
import { apiClient } from '../apiClient'

export interface FetchedBlob {
  url: string
  contentType: string
}

function uploadFile(url: string, file: File) {
  const formData = new FormData()
  formData.append('file', file)
  // Content-Type intentionally left unset - axios/the browser sets the multipart boundary
  // automatically for FormData bodies; a hardcoded header here would break parsing server-side.
  return apiClient.post(url, formData).then(() => undefined)
}

/**
 * Files are served through an authenticated endpoint now (not a plain static URL), and this
 * app's auth is a Bearer token in localStorage, not a cookie - a plain <img src="..."> can't
 * carry that header. So we fetch via apiClient (which attaches it) as a blob, then hand the
 * browser an object URL. Returns null on 404 (no file uploaded yet) rather than throwing, since
 * "nothing uploaded" is an expected, common state, not an error. Returns the blob's content type
 * alongside the URL so callers (e.g. FilePreviewModal) can pick an <img> vs <iframe> renderer
 * without a second request.
 *
 * Callers must revoke the returned URL (URL.revokeObjectURL) when it's replaced/unmounted.
 */
async function fetchBlobUrl(url: string): Promise<FetchedBlob | null> {
  try {
    const response = await apiClient.get(url, { responseType: 'blob' })
    const blob = response.data as Blob
    return { url: URL.createObjectURL(blob), contentType: blob.type }
  } catch (err) {
    if (isAxiosError(err) && err.response?.status === 404) return null
    throw err
  }
}

export const uploadApi = {
  uploadMyPhoto: (file: File) => uploadFile('/api/members/me/photo', file),
  uploadMyPrcId: (file: File) => uploadFile('/api/members/me/prc-id', file),
  fetchMyPhotoUrl: () => fetchBlobUrl('/api/members/me/photo'),
  fetchMyPrcIdUrl: () => fetchBlobUrl('/api/members/me/prc-id'),
  /** Admin viewing (members:view permission) - used by the PRC Verifications review queue. */
  fetchMemberPrcIdUrl: (memberId: string) => fetchBlobUrl(`/api/members/${memberId}/prc-id`),
}
