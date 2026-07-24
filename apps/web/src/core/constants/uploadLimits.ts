// Matches the backend's MemberUploadService caps - checked client-side first so an oversized
// file gets an immediate, friendly message instead of a round trip that risks a raw connection
// reset (Kestrel aborts the connection when a request exceeds its size limit mid-body-read,
// rather than returning a clean 4xx - see MembersController's upload endpoints).
export const MAX_IMAGE_BYTES = 24 * 1024 * 1024
export const MAX_PDF_BYTES = 2 * 1024 * 1024
