import { useEffect, useState } from 'react'
import { LuAward } from 'react-icons/lu'
import type { MyCpdSummary } from '../api/endpoints/eventApi'
import { eventApi } from '../api/endpoints/eventApi'
import { describeError } from '../utils/apiError'
import { StatTile } from '../../integrations/template/components/shared/StatTile'
import { MyCpdTable, PageBreadcrumb, PageMeta } from '../../integrations/template'

export function MyCpdPage() {
  const [summary, setSummary] = useState<MyCpdSummary | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    eventApi
      .getMyCpd()
      .then((result) => {
        if (!cancelled) setSummary(result)
      })
      .catch((err) => {
        if (!cancelled) setError(describeError(err, 'Could not load your CPD history.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  return (
    <>
      <PageMeta title="My CPD" />
      <main>
        <PageBreadcrumb title="My CPD" />

        {error && <p className="text-sm text-danger mb-4">{error}</p>}

        {loading || !summary ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          <div className="flex flex-col gap-4">
            <div className="card">
              <div className="card-body">
                <div className="grid grid-cols-1 sm:grid-cols-3">
                  <StatTile
                    icon={LuAward}
                    label="Total CPD units earned"
                    value={summary.totalCreditUnits}
                    accent="bg-primary/15 text-primary"
                  />
                </div>
              </div>
            </div>
            <MyCpdTable registrations={summary.registrations} />
          </div>
        )}
      </main>
    </>
  )
}
