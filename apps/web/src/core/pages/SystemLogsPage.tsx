import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { systemLogsApi, type AuditLogEntry, type ErrorLogEntry } from '../api/endpoints/systemLogsApi'
import { PageBreadcrumb, PageMeta, AuditLogTable, ErrorLogTable } from '../../integrations/template'

const PAGE_SIZE = 20

export function SystemLogsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const activeTab = searchParams.get('tab') === 'errors' ? 'errors' : 'audit'

  const [auditEntries, setAuditEntries] = useState<AuditLogEntry[]>([])
  const [errorEntries, setErrorEntries] = useState<ErrorLogEntry[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [auditLoading, setAuditLoading] = useState(true)
  const [errorsLoading, setErrorsLoading] = useState(true)
  const loading = activeTab === 'audit' ? auditLoading : errorsLoading

  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [eventTypeFilter, setEventTypeFilter] = useState('')
  const [sourceFilter, setSourceFilter] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')

  // Debounces typing into the search box - the audit fetch effect only re-runs off `search`, not
  // `searchInput`, so a fetch fires once per pause in typing rather than per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => {
      setSearch(searchInput)
      setPage(1)
    }, 350)
    return () => clearTimeout(timer)
  }, [searchInput])

  useEffect(() => {
    if (activeTab !== 'audit') return
    let cancelled = false
    setAuditLoading(true)
    systemLogsApi
      .getAuditLog({
        page,
        pageSize: PAGE_SIZE,
        ...(search ? { search } : {}),
        ...(eventTypeFilter ? { eventType: eventTypeFilter } : {}),
        ...(from ? { from } : {}),
        ...(to ? { to } : {}),
      })
      .then((result) => {
        if (cancelled) return
        setAuditEntries(result.items)
        setTotalCount(result.totalCount)
      })
      .finally(() => {
        if (!cancelled) setAuditLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [activeTab, page, search, eventTypeFilter, from, to])

  useEffect(() => {
    if (activeTab !== 'errors') return
    let cancelled = false
    setErrorsLoading(true)
    systemLogsApi
      .getErrorLog({
        page,
        pageSize: PAGE_SIZE,
        ...(search ? { search } : {}),
        ...(sourceFilter ? { source: Number(sourceFilter) as 0 | 1 } : {}),
        ...(from ? { from } : {}),
        ...(to ? { to } : {}),
      })
      .then((result) => {
        if (cancelled) return
        setErrorEntries(result.items)
        setTotalCount(result.totalCount)
      })
      .finally(() => {
        if (!cancelled) setErrorsLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [activeTab, page, search, sourceFilter, from, to])

  const handleTabChange = (tab: 'audit' | 'errors') => {
    setSearchParams(tab === 'audit' ? {} : { tab })
    setPage(1)
  }

  return (
    <>
      <PageMeta title="System Logs" />
      <main>
        <PageBreadcrumb title="System Logs" />
        <div className="flex gap-2 mb-4" role="tablist" aria-label="System log views">
          <button
            type="button"
            role="tab"
            aria-selected={activeTab === 'audit'}
            className={`btn btn-sm ${activeTab === 'audit' ? 'bg-primary text-white' : 'border border-default-300'}`}
            onClick={() => handleTabChange('audit')}
          >
            Audit
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={activeTab === 'errors'}
            className={`btn btn-sm ${activeTab === 'errors' ? 'bg-primary text-white' : 'border border-default-300'}`}
            onClick={() => handleTabChange('errors')}
          >
            Errors
          </button>
        </div>
        {loading ? (
          <p className="text-sm text-default-500">Loading…</p>
        ) : (
          activeTab === 'audit' ? (
            <AuditLogTable
              entries={auditEntries}
              searchInput={searchInput}
              onSearchInputChange={setSearchInput}
              eventTypeFilter={eventTypeFilter}
              onEventTypeFilterChange={(value) => {
                setEventTypeFilter(value)
                setPage(1)
              }}
              from={from}
              to={to}
              onFromChange={(value) => {
                setFrom(value)
                setPage(1)
              }}
              onToChange={(value) => {
                setTo(value)
                setPage(1)
              }}
              page={page}
              pageSize={PAGE_SIZE}
              totalCount={totalCount}
              onPageChange={setPage}
            />
          ) : (
            <ErrorLogTable
              entries={errorEntries}
              searchInput={searchInput}
              onSearchInputChange={setSearchInput}
              sourceFilter={sourceFilter}
              onSourceFilterChange={(value) => {
                setSourceFilter(value)
                setPage(1)
              }}
              from={from}
              to={to}
              onFromChange={(value) => {
                setFrom(value)
                setPage(1)
              }}
              onToChange={(value) => {
                setTo(value)
                setPage(1)
              }}
              page={page}
              pageSize={PAGE_SIZE}
              totalCount={totalCount}
              onPageChange={setPage}
            />
          )
        )}
      </main>
    </>
  )
}
