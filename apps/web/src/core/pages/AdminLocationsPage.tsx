import { useCallback, useEffect, useState } from 'react'
import {
  locationsAdminApi,
  type CityItem,
  type ProvinceItem,
  type RegionItem,
} from '../api/endpoints/locationsApi'
import { describeError } from '../utils/apiError'
import { invalidateLocations } from '../utils/phLocations'
import {
  ConfirmationModal,
  LocationFormModal,
  LocationsTable,
  PageBreadcrumb,
  PageMeta,
  Toast,
  type LocationFormInitial,
  type LocationFormValues,
  type LocationKind,
  type ToastStatus,
} from '../../integrations/template'

const PAGE_SIZE = 20

type Row = RegionItem | ProvinceItem | CityItem

const TABS: { kind: LocationKind; label: string }[] = [
  { kind: 'cities', label: 'Cities & municipalities' },
  { kind: 'provinces', label: 'Provinces' },
  { kind: 'regions', label: 'Regions' },
]

/** Super Admin maintenance of the places offered by the address picker. */
export function AdminLocationsPage() {
  const [tab, setTab] = useState<LocationKind>('cities')
  const [rows, setRows] = useState<Row[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [loading, setLoading] = useState(true)
  const [page, setPage] = useState(1)
  const [reloadKey, setReloadKey] = useState(0)

  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [regionFilter, setRegionFilter] = useState('')
  const [provinceFilter, setProvinceFilter] = useState('')

  const [regions, setRegions] = useState<RegionItem[]>([])
  const [provinceOptions, setProvinceOptions] = useState<ProvinceItem[]>([])

  const [form, setForm] = useState<{ initial: LocationFormInitial | null } | null>(null)
  const [deleting, setDeleting] = useState<Row | null>(null)
  const [toast, setToast] = useState<ToastStatus | null>(null)

  // Debounced like SystemLogsPage: one fetch per pause in typing, not per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => {
      setSearch(searchInput)
      setPage(1)
    }, 350)
    return () => clearTimeout(timer)
  }, [searchInput])

  const reload = useCallback(() => {
    invalidateLocations()
    setReloadKey((k) => k + 1)
  }, [])

  // The region list backs every dropdown, so it's reloaded after any change.
  useEffect(() => {
    let cancelled = false
    void locationsAdminApi.getRegions().then((result) => {
      if (!cancelled) setRegions(result)
    })
    return () => {
      cancelled = true
    }
  }, [reloadKey])

  // Province filter options follow the selected region (Cities tab only).
  useEffect(() => {
    if (!regionFilter) {
      setProvinceOptions([])
      return
    }
    let cancelled = false
    void locationsAdminApi.getProvinces({ regionId: regionFilter, pageSize: 100 }).then((result) => {
      if (!cancelled) setProvinceOptions(result.items)
    })
    return () => {
      cancelled = true
    }
  }, [regionFilter, reloadKey])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    const request =
      tab === 'regions'
        ? locationsAdminApi.getRegions(search || undefined).then((items) => ({ items, totalCount: items.length }))
        : tab === 'provinces'
          ? locationsAdminApi.getProvinces({
              page,
              pageSize: PAGE_SIZE,
              ...(search ? { search } : {}),
              ...(regionFilter ? { regionId: regionFilter } : {}),
            })
          : locationsAdminApi.getCities({
              page,
              pageSize: PAGE_SIZE,
              ...(search ? { search } : {}),
              ...(regionFilter ? { regionId: regionFilter } : {}),
              ...(provinceFilter ? { provinceId: provinceFilter } : {}),
            })
    request
      .then((result) => {
        if (cancelled) return
        setRows(result.items as Row[])
        setTotalCount(result.totalCount)
      })
      .catch((err) => {
        if (!cancelled) setToast({ ok: false, text: describeError(err, 'Could not load locations.') })
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [tab, page, search, regionFilter, provinceFilter, reloadKey])

  const handleTabChange = (next: LocationKind) => {
    setTab(next)
    setPage(1)
    setRows([])
    setSearchInput('')
    setSearch('')
    setProvinceFilter('')
    if (next === 'regions') setRegionFilter('')
  }

  const toInitial = (row: Row): LocationFormInitial => {
    if (tab === 'regions') {
      const r = row as RegionItem
      return { id: r.id, name: r.name, sortOrder: r.sortOrder }
    }
    if (tab === 'provinces') {
      const p = row as ProvinceItem
      return { id: p.id, name: p.name, regionId: p.regionId }
    }
    const c = row as CityItem
    return { id: c.id, name: c.name, regionId: c.regionId, provinceId: c.provinceId, zipCode: c.zipCode }
  }

  const handleSave = async (values: LocationFormValues) => {
    const id = form?.initial?.id
    try {
      if (tab === 'regions') {
        const body = { name: values.name, sortOrder: values.sortOrder }
        await (id ? locationsAdminApi.updateRegion(id, body) : locationsAdminApi.createRegion(body))
      } else if (tab === 'provinces') {
        const body = { name: values.name, regionId: values.regionId }
        await (id ? locationsAdminApi.updateProvince(id, body) : locationsAdminApi.createProvince(body))
      } else {
        const body = { name: values.name, zipCode: values.zipCode || null, provinceId: values.provinceId }
        await (id ? locationsAdminApi.updateCity(id, body) : locationsAdminApi.createCity(body))
      }
    } catch (err) {
      // The modal shows this next to the form so a duplicate-name conflict can be fixed in place.
      throw new Error(describeError(err, 'Could not save.'))
    }
    setForm(null)
    setToast({ ok: true, text: `Saved ${values.name}.` })
    reload()
  }

  const handleDelete = async () => {
    if (!deleting) return
    const target = deleting
    try {
      if (tab === 'regions') await locationsAdminApi.deleteRegion(target.id)
      else if (tab === 'provinces') await locationsAdminApi.deleteProvince(target.id)
      else await locationsAdminApi.deleteCity(target.id)
      setToast({ ok: true, text: `Deleted ${target.name}.` })
      reload()
    } catch (err) {
      setToast({ ok: false, text: describeError(err, 'Could not delete.') })
    } finally {
      setDeleting(null)
    }
  }

  return (
    <>
      <PageMeta title="Locations" />
      <main>
        <PageBreadcrumb title="Locations" />
        <p className="text-sm text-default-600 mb-4">
          The regions, provinces and cities offered in every address form. Changes apply right away.
          Addresses members have already saved are not changed.
        </p>
        <div className="flex gap-2 mb-4" role="tablist" aria-label="Location levels">
          {TABS.map(({ kind, label }) => (
            <button
              key={kind}
              type="button"
              role="tab"
              aria-selected={tab === kind}
              className={`btn btn-sm ${tab === kind ? 'bg-primary text-white' : 'border border-default-300'}`}
              onClick={() => handleTabChange(kind)}
            >
              {label}
            </button>
          ))}
        </div>

        <LocationsTable
          kind={tab}
          rows={rows}
          loading={loading}
          searchInput={searchInput}
          onSearchInputChange={setSearchInput}
          regions={regions}
          regionFilter={regionFilter}
          onRegionFilterChange={(value) => {
            setRegionFilter(value)
            setProvinceFilter('')
            setPage(1)
          }}
          provinceOptions={provinceOptions}
          provinceFilter={provinceFilter}
          onProvinceFilterChange={(value) => {
            setProvinceFilter(value)
            setPage(1)
          }}
          page={page}
          pageSize={PAGE_SIZE}
          totalCount={totalCount}
          onPageChange={setPage}
          onAdd={() => setForm({ initial: null })}
          onEdit={(row) => setForm({ initial: toInitial(row) })}
          onDelete={setDeleting}
        />
      </main>

      {form && (
        <LocationFormModal
          kind={tab}
          initial={form.initial}
          regions={regions}
          defaultRegionId={regionFilter}
          defaultProvinceId={provinceFilter}
          onSave={handleSave}
          onCancel={() => setForm(null)}
        />
      )}

      <ConfirmationModal
        isOpen={deleting !== null}
        title={`Delete ${deleting?.name ?? ''}?`}
        message="It will no longer be offered in address forms. Addresses members already saved are not changed."
        confirmLabel="Delete"
        onConfirm={handleDelete}
        onCancel={() => setDeleting(null)}
      />
      <Toast status={toast} onDismiss={() => setToast(null)} />
    </>
  )
}
