import { useEffect, useState, type FormEvent } from 'react'
import { locationsAdminApi, type ProvinceItem, type RegionItem } from '../../../core/api/endpoints/locationsApi'
import { StandardButton } from '../components/shared/StandardButton'

export type LocationKind = 'regions' | 'provinces' | 'cities'

/** Everything the three forms can produce; the page picks the fields its tab needs. */
export interface LocationFormValues {
  name: string
  sortOrder: number | null
  regionId: string
  provinceId: string
  zipCode: string
}

export interface LocationFormInitial {
  id: string
  name: string
  sortOrder?: number
  regionId?: string
  provinceId?: string
  zipCode?: string | null
}

interface LocationFormModalProps {
  kind: LocationKind
  /** Null means create. */
  initial: LocationFormInitial | null
  regions: RegionItem[]
  /** Pre-selects region/province from the table filters on create. */
  defaultRegionId: string
  defaultProvinceId: string
  onSave: (values: LocationFormValues) => Promise<void>
  onCancel: () => void
}

const NOUN: Record<LocationKind, string> = {
  regions: 'region',
  provinces: 'province',
  cities: 'city or municipality',
}

export const LocationFormModal = ({
  kind,
  initial,
  regions,
  defaultRegionId,
  defaultProvinceId,
  onSave,
  onCancel,
}: LocationFormModalProps) => {
  const [name, setName] = useState(initial?.name ?? '')
  const [sortOrder, setSortOrder] = useState(initial?.sortOrder != null ? String(initial.sortOrder) : '')
  const [regionId, setRegionId] = useState(initial?.regionId ?? defaultRegionId)
  const [provinceId, setProvinceId] = useState(initial?.provinceId ?? defaultProvinceId)
  const [zipCode, setZipCode] = useState(initial?.zipCode ?? '')
  const [provinces, setProvinces] = useState<ProvinceItem[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const noun = NOUN[kind]
  const needsRegion = kind !== 'regions'
  const needsProvince = kind === 'cities'

  // The province dropdown only offers the chosen region's provinces.
  useEffect(() => {
    if (!needsProvince || !regionId) {
      setProvinces([])
      return
    }
    let cancelled = false
    void locationsAdminApi.getProvinces({ regionId, pageSize: 100 }).then((result) => {
      if (!cancelled) setProvinces(result.items)
    })
    return () => {
      cancelled = true
    }
  }, [needsProvince, regionId])

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onCancel()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onCancel])

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    if (!name.trim()) return setError('Name is required.')
    if (needsRegion && !regionId) return setError('Choose a region.')
    if (needsProvince && !provinceId) return setError('Choose a province.')

    setSaving(true)
    setError(null)
    try {
      await onSave({
        name: name.trim(),
        sortOrder: sortOrder.trim() === '' ? null : Number(sortOrder),
        regionId,
        provinceId,
        zipCode: zipCode.trim(),
      })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save.')
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-100 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/50" onClick={onCancel} />
      <form className="relative card w-full max-w-md" onSubmit={(e) => void handleSubmit(e)}>
        <div className="card-header">
          <h6 className="card-title">
            {initial ? 'Edit' : 'Add'} {noun}
          </h6>
        </div>
        <div className="card-body flex flex-col gap-4 text-sm">
          {needsRegion && (
            <div>
              <label htmlFor="loc-region" className="block font-medium text-default-900 text-sm mb-2">
                Region
              </label>
              <select
                id="loc-region"
                className="form-input"
                value={regionId}
                onChange={(e) => {
                  setRegionId(e.target.value)
                  setProvinceId('')
                }}
              >
                <option value="">Select a region…</option>
                {regions.map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.name}
                  </option>
                ))}
              </select>
            </div>
          )}
          {needsProvince && (
            <div>
              <label htmlFor="loc-province" className="block font-medium text-default-900 text-sm mb-2">
                Province
              </label>
              <select
                id="loc-province"
                className="form-input"
                value={provinceId}
                disabled={!regionId}
                onChange={(e) => setProvinceId(e.target.value)}
              >
                <option value="">{regionId ? 'Select a province…' : 'Choose a region first'}</option>
                {provinces.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </select>
            </div>
          )}
          <div>
            <label htmlFor="loc-name" className="block font-medium text-default-900 text-sm mb-2">
              Name
            </label>
            <input
              id="loc-name"
              className="form-input"
              value={name}
              maxLength={160}
              autoFocus
              onChange={(e) => setName(e.target.value)}
            />
          </div>
          {kind === 'cities' && (
            <div>
              <label htmlFor="loc-zip" className="block font-medium text-default-900 text-sm mb-2">
                Zip code (optional)
              </label>
              <input
                id="loc-zip"
                className="form-input"
                value={zipCode}
                maxLength={16}
                onChange={(e) => setZipCode(e.target.value)}
              />
            </div>
          )}
          {kind === 'regions' && (
            <div>
              <label htmlFor="loc-sort" className="block font-medium text-default-900 text-sm mb-2">
                List order (optional)
              </label>
              <input
                id="loc-sort"
                type="number"
                className="form-input"
                value={sortOrder}
                placeholder="Leave blank to put it last"
                onChange={(e) => setSortOrder(e.target.value)}
              />
            </div>
          )}
          {error && (
            <p role="alert" className="text-sm text-danger">
              {error}
            </p>
          )}
        </div>
        <div className="card-footer flex items-center justify-end gap-2">
          <StandardButton variant="secondary" onClick={onCancel} disabled={saving}>
            Cancel
          </StandardButton>
          <StandardButton variant="primary" type="submit" loading={saving} loadingLabel="Saving…">
            Save
          </StandardButton>
        </div>
      </form>
    </div>
  )
}
