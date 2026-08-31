import { useState } from 'react'
import { LuDownload } from 'react-icons/lu'
import type { MyCpdRegistration } from '../../../core/api/endpoints/eventApi'
import { eventApi } from '../../../core/api/endpoints/eventApi'
import { StandardButton } from '../components/shared/StandardButton'

interface MyCpdTableProps {
  registrations: MyCpdRegistration[]
}

/** Table structure mirrors EventRosterTable's verified pattern (overflow-x-auto > min-w-full
 *  inline-block align-middle > overflow-hidden > table.min-w-full divide-y divide-default-200,
 *  bg-default-150 header) rather than the plan's literal `className="table"`, which doesn't exist
 *  in this codebase. */
export function MyCpdTable({ registrations }: MyCpdTableProps) {
  // Keyed per-row (not a single bool) so clicking Download on one row only shows loading on that
  // row's button - also doubles as the double-click guard, since StandardButton disables itself
  // while `loading`. `error` is a plain string, not keyed - only one download can be in flight at
  // a time (buttons for other rows stay clickable, but a stale message from a different row is an
  // acceptable trade-off for the same reason EventRegisterModal/EventRosterPage use one error slot).
  const [downloadingId, setDownloadingId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const handleDownload = async (registrationId: string) => {
    setError(null)
    setDownloadingId(registrationId)
    try {
      const result = await eventApi.downloadCertificate(registrationId)
      if (!result) {
        // downloadCertificate returns null on any failure - a network error, or a race where this
        // page's CPD summary is stale and the certificate isn't actually finalized server-side yet.
        // Mirrors FilePreviewModal's identical null-on-failure handling for paymentApi.fetchProofUrl.
        setError('Could not download the certificate. Please try again.')
        return
      }
      window.open(result.url, '_blank')
    } catch {
      setError('Could not download the certificate. Please try again.')
    } finally {
      setDownloadingId(null)
    }
  }

  if (registrations.length === 0) {
    return <p className="text-sm text-default-500">You haven't registered for any events yet.</p>
  }

  return (
    <div className="card">
      {error && <p className="text-sm text-danger px-4 pt-4">{error}</p>}
      <div className="flex flex-col">
        <div className="overflow-x-auto">
          <div className="min-w-full inline-block align-middle">
            <div className="overflow-hidden">
              <table className="min-w-full divide-y divide-default-200">
                <thead className="bg-default-150">
                  <tr className="text-sm font-normal text-default-700 whitespace-nowrap">
                    <th className="px-3.5 py-3 text-start">Event</th>
                    <th className="px-3.5 py-3 text-start">Mode</th>
                    <th className="px-3.5 py-3 text-start">Status</th>
                    <th className="px-3.5 py-3 text-start">Sessions Attended</th>
                    <th className="px-3.5 py-3 text-start">Credit Earned</th>
                    <th className="px-3.5 py-3 text-start">Certificate</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-default-200">
                  {registrations.map((r) => (
                    <tr key={r.registrationId} className="text-default-800 font-normal text-sm whitespace-nowrap">
                      <td className="py-3 px-3.5">
                        <div className="font-semibold">{r.eventTitle}</div>
                        <div className="text-xs text-default-400">{new Date(r.eventStartsAt).toLocaleDateString()}</div>
                      </td>
                      <td className="py-3 px-3.5">{r.mode}</td>
                      <td className="py-3 px-3.5">{r.status}</td>
                      <td className="py-3 px-3.5">
                        {r.sessionsAttended} / {r.totalSessions}
                      </td>
                      <td className="py-3 px-3.5">{r.creditUnits ?? '-'}</td>
                      <td className="py-3 px-3.5">
                        {r.creditUnits !== null ? (
                          <StandardButton
                            variant="secondary"
                            size="sm"
                            icon={LuDownload}
                            loading={downloadingId === r.registrationId}
                            loadingLabel="Downloading…"
                            onClick={() => handleDownload(r.registrationId)}
                          >
                            Download
                          </StandardButton>
                        ) : (
                          <span className="text-xs text-default-400">Not yet available</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
