import { useEffect, useState } from 'react'
import {
  findRegionFor,
  getAllProvinces,
  getCities,
  getCountries,
  getProvinces,
  getRegions,
  getZipCode,
} from '../../../../core/utils/phLocations'
import { SearchableSelect } from './SearchableSelect'

export interface AddressValue {
  houseNo: string
  street: string
  barangay: string
  cityMunicipality: string
  province: string
  zipCode: string
  country: string
}

interface PhilippineAddressFieldsProps {
  value: AddressValue
  onChange: (field: keyof AddressValue, next: string) => void
  /** False renders read-only text instead of inputs, matching the profile tab's view mode. */
  editing?: boolean
  /** The wizard marks residence fields required; the admin form deliberately doesn't. */
  required?: boolean
  /** Distinguishes residence from mailing so the two label/input pairs on one page stay unique. */
  idPrefix: string
  gridClassName?: string
}

const DEFAULT_GRID = 'grid grid-cols-1 md:grid-cols-2 2xl:grid-cols-3 min-[1800px]:grid-cols-4 gap-4 text-sm'
const LABEL = 'block font-medium text-default-900 text-sm mb-2'
const VALUE = 'font-semibold text-default-800'

/** Keeps a previously-saved free-text value selectable even when it isn't in the reference data. */
function withFallback(options: string[], current: string): string[] {
  if (!current || options.some((o) => o === current)) return options
  return [current, ...options]
}

/**
 * Region → Province → City cascade over the bundled PSGC dataset, with ZIP auto-filled on city
 * selection and Country defaulted to Philippines.
 *
 * Region is deliberately NOT a stored field - it exists only to narrow the province list, so it's
 * local state here, recovered from the saved province/city on mount via `findRegionFor`. That
 * recovery matters: every member who registered before this shipped has plain typed strings with
 * no region, and without it their saved address would render as empty dropdowns and read as wiped.
 * When a saved value genuinely isn't in the reference data, it's kept as a selectable option and
 * flagged rather than silently dropped.
 *
 * Changing Region clears Province/City/ZIP, and changing Province clears City/ZIP - deliberate,
 * since a stale child value under a new parent would be a nonsense address.
 */
export const PhilippineAddressFields = ({
  value,
  onChange,
  editing = true,
  required = false,
  idPrefix,
  gridClassName = DEFAULT_GRID,
}: PhilippineAddressFieldsProps) => {
  const [region, setRegion] = useState('')
  const [regions, setRegions] = useState<string[]>([])
  const [provinces, setProvinces] = useState<string[]>([])
  const [cities, setCities] = useState<string[]>([])
  const [countries, setCountries] = useState<string[]>([])
  const [unmatched, setUnmatched] = useState(false)

  // Resolve the region for an already-saved address once, on first render in edit mode.
  useEffect(() => {
    let cancelled = false
    if (!editing) return
    void (async () => {
      const [loadedRegions, loadedCountries] = await Promise.all([getRegions(), getCountries()])
      if (cancelled) return
      setRegions(loadedRegions)
      setCountries(loadedCountries)

      if (!value.province) return
      const matched = await findRegionFor(value.province, value.cityMunicipality)
      if (cancelled) return
      if (matched) {
        setRegion(matched)
      } else {
        setUnmatched(true)
      }
    })()
    return () => {
      cancelled = true
    }
    // Intentionally mount-only: re-running on every keystroke would fight the user's own selections.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editing])

  // With no region picked, offer every province rather than nothing - the box is type-to-search, so
  // someone who knows their province shouldn't have to work out its region first. handleProvince
  // back-fills the region from whatever they choose.
  useEffect(() => {
    let cancelled = false
    void (region ? getProvinces(region) : getAllProvinces()).then((next) => {
      if (!cancelled) setProvinces(next)
    })
    return () => {
      cancelled = true
    }
  }, [region])

  useEffect(() => {
    let cancelled = false
    void getCities(region, value.province).then((next) => {
      if (!cancelled) setCities(next)
    })
    return () => {
      cancelled = true
    }
  }, [region, value.province])

  const handleRegion = (next: string) => {
    setRegion(next)
    setUnmatched(false)
    onChange('province', '')
    onChange('cityMunicipality', '')
    onChange('zipCode', '')
  }

  const handleProvince = async (next: string) => {
    onChange('province', next)
    // Any edit here invalidates the city below it, including a partial one mid-typing - the old
    // city can't belong to whatever province is being typed.
    onChange('cityMunicipality', '')
    onChange('zipCode', '')
    if (!next) return

    // Typing a province straight in (no region picked, or a different region's province) still
    // needs a region for the city lookup - derive it instead of making the applicant backtrack.
    const matched = await findRegionFor(next, '')
    if (matched) {
      setRegion(matched)
      setUnmatched(false)
    }
  }

  const handleCity = async (next: string) => {
    onChange('cityMunicipality', next)
    const zip = await getZipCode(region, value.province, next)
    // 76 of 1,647 cities have no mapped ZIP - leave whatever's there rather than blanking it.
    if (zip) onChange('zipCode', zip)
  }

  if (!editing) {
    return (
      <div className={gridClassName}>
        <div>
          <span className={LABEL}>House No.</span>
          <span className={VALUE}>{value.houseNo || '-'}</span>
        </div>
        <div>
          <span className={LABEL}>Street</span>
          <span className={VALUE}>{value.street || '-'}</span>
        </div>
        <div>
          <span className={LABEL}>Barangay</span>
          <span className={VALUE}>{value.barangay || '-'}</span>
        </div>
        <div>
          <span className={LABEL}>City or Municipality</span>
          <span className={VALUE}>{value.cityMunicipality || '-'}</span>
        </div>
        <div>
          <span className={LABEL}>Province</span>
          <span className={VALUE}>{value.province || '-'}</span>
        </div>
        <div>
          <span className={LABEL}>Zip Code</span>
          <span className={VALUE}>{value.zipCode || '-'}</span>
        </div>
        <div>
          <span className={LABEL}>Country</span>
          <span className={VALUE}>{value.country || '-'}</span>
        </div>
      </div>
    )
  }

  return (
    <>
      {unmatched && (
        <p className="text-xs text-warning bg-warning/10 rounded-lg px-3 py-2 mb-3">
          This address was saved before the location picker existed and doesn't match a known
          province. Your saved values are kept below — pick a Region to update them.
        </p>
      )}
      <div className={gridClassName}>
        <div>
          <label htmlFor={`${idPrefix}-house-no`} className={LABEL}>
            House No.
          </label>
          <input
            id={`${idPrefix}-house-no`}
            className="form-input"
            value={value.houseNo}
            onChange={(e) => onChange('houseNo', e.target.value)}
          />
        </div>
        <div>
          <label htmlFor={`${idPrefix}-street`} className={LABEL}>
            Street
          </label>
          <input
            id={`${idPrefix}-street`}
            className="form-input"
            required={required}
            value={value.street}
            onChange={(e) => onChange('street', e.target.value)}
          />
        </div>
        <div>
          <label htmlFor={`${idPrefix}-barangay`} className={LABEL}>
            Barangay
          </label>
          <input
            id={`${idPrefix}-barangay`}
            className="form-input"
            required={required}
            value={value.barangay}
            onChange={(e) => onChange('barangay', e.target.value)}
          />
        </div>
        <div>
          <label htmlFor={`${idPrefix}-region`} className={LABEL}>
            Region
          </label>
          <SearchableSelect
            id={`${idPrefix}-region`}
            required={required}
            placeholder="Type to search…"
            value={region}
            options={regions}
            onChange={handleRegion}
          />
        </div>
        <div>
          <label htmlFor={`${idPrefix}-province`} className={LABEL}>
            Province
          </label>
          {/* Not gated on Region any more - the list falls back to all provinces, and picking one
              back-fills the region. */}
          <SearchableSelect
            id={`${idPrefix}-province`}
            required={required}
            placeholder="Type to search…"
            value={value.province}
            options={withFallback(provinces, value.province)}
            onChange={(next) => void handleProvince(next)}
          />
        </div>
        <div>
          <label htmlFor={`${idPrefix}-city`} className={LABEL}>
            City or Municipality
          </label>
          <SearchableSelect
            id={`${idPrefix}-city`}
            required={required}
            disabled={!value.province}
            placeholder={value.province ? 'Type to search…' : 'Enter a province first'}
            value={value.cityMunicipality}
            options={withFallback(cities, value.cityMunicipality)}
            onChange={(next) => void handleCity(next)}
          />
        </div>
        <div>
          <label htmlFor={`${idPrefix}-zip`} className={LABEL}>
            Zip Code
          </label>
          {/* Auto-filled from the selected city but never locked - some cities legitimately have
              several ZIPs, and 76 have none on record. */}
          <input
            id={`${idPrefix}-zip`}
            className="form-input"
            required={required}
            value={value.zipCode}
            onChange={(e) => onChange('zipCode', e.target.value)}
          />
        </div>
        <div>
          <label htmlFor={`${idPrefix}-country`} className={LABEL}>
            Country
          </label>
          <SearchableSelect
            id={`${idPrefix}-country`}
            required={required}
            placeholder="Type to search…"
            value={value.country}
            options={withFallback(countries, value.country)}
            onChange={(next) => onChange('country', next)}
          />
        </div>
      </div>
    </>
  )
}
