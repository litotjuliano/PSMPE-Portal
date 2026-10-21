import { locationsApi } from '../api/endpoints/locationsApi'

/* A city's zipCode is null when it has no well-documented ZIP. Expected data, not a defect: the UI
 * leaves ZIP blank and editable. */

interface PhLocationIndex {
  regions: string[]
  /** region -> ordered province names */
  provinces: Map<string, string[]>
  /** "region|province" -> ordered city names */
  cities: Map<string, string[]>
  /** "region|province|city" -> zip (or null) */
  zips: Map<string, string | null>
  /** Country list, bundled alongside since both are only needed by the address form. */
  countries: string[]
}

let indexPromise: Promise<PhLocationIndex> | null = null

/**
 * Loads and indexes the Philippine locations, which a Super Admin maintains in the database
 * (GET /api/locations). Countries stay a bundled file.
 *
 * The promise is cached, so concurrent callers (residence + mailing rendering together) share one
 * fetch. A failed load clears the cache so the next call retries instead of staying broken, and
 * `invalidateLocations` clears it after a Super Admin edit so the picker isn't stale.
 */
function loadIndex(): Promise<PhLocationIndex> {
  if (indexPromise) return indexPromise
  const promise = (async () => {
    const [rows, countriesModule] = await Promise.all([locationsApi.getAll(), import('../../data/countries.json')])
    const countries = countriesModule.default as string[]

    const regions: string[] = []
    const provinces = new Map<string, string[]>()
    const cities = new Map<string, string[]>()
    const zips = new Map<string, string | null>()

    for (const row of rows) {
      if (!provinces.has(row.region)) {
        regions.push(row.region)
        provinces.set(row.region, [])
      }
      const provinceKey = `${row.region}|${row.province}`
      const regionProvinces = provinces.get(row.region)!
      if (!cities.has(provinceKey)) {
        regionProvinces.push(row.province)
        cities.set(provinceKey, [])
      }
      cities.get(provinceKey)!.push(row.city)
      zips.set(`${provinceKey}|${row.city}`, row.zipCode)
    }

    return { regions, provinces, cities, zips, countries }
  })()
  indexPromise = promise
  promise.catch(() => {
    if (indexPromise === promise) indexPromise = null
  })
  return promise
}

/** Drops the cached index so the next lookup refetches - call after editing locations. */
export function invalidateLocations(): void {
  indexPromise = null
}

export async function getRegions(): Promise<string[]> {
  return (await loadIndex()).regions
}

export async function getProvinces(region: string): Promise<string[]> {
  if (!region) return []
  return (await loadIndex()).provinces.get(region) ?? []
}

/**
 * Every province across every region, sorted.
 *
 * Backs the Province box before a Region has been picked: the field is type-to-search, so someone
 * who knows their province shouldn't have to work out which region it's in first. The region is
 * then back-filled from the choice via `findRegionFor`.
 */
export async function getAllProvinces(): Promise<string[]> {
  const index = await loadIndex()
  const all = new Set<string>()
  for (const names of index.provinces.values()) {
    for (const name of names) all.add(name)
  }
  return [...all].sort((a, b) => a.localeCompare(b))
}

const norm = (v: string) => v.trim().toLowerCase()

/** The dataset's spelling of a province within a region, matched case- and whitespace-insensitively.
 *  A hand-typed "zamboanga del sur" must still find its cities. */
function canonicalProvince(index: PhLocationIndex, region: string, province: string): string | null {
  return (index.provinces.get(region) ?? []).find((p) => norm(p) === norm(province)) ?? null
}

export async function getCities(region: string, province: string): Promise<string[]> {
  if (!region || !province) return []
  const index = await loadIndex()
  const canonical = canonicalProvince(index, region, province)
  return canonical ? (index.cities.get(`${region}|${canonical}`) ?? []) : []
}

/** Null both when the city genuinely has no mapped ZIP and when the city isn't found at all -
 *  callers treat both the same way (leave whatever the user typed alone). */
export async function getZipCode(region: string, province: string, city: string): Promise<string | null> {
  if (!region || !province || !city) return null
  const index = await loadIndex()
  const canonical = canonicalProvince(index, region, province)
  if (!canonical) return null
  return index.zips.get(`${region}|${canonical}|${city}`) ?? null
}

export async function getCountries(): Promise<string[]> {
  return (await loadIndex()).countries
}

/**
 * Finds which region/province a previously-saved free-text city belongs to.
 *
 * Needed because every member who registered before the cascade shipped has plain typed strings
 * with no region recorded - without this their saved city/province would render as an empty
 * dropdown and look wiped. Matching is case- and whitespace-insensitive; returns null when the
 * saved value doesn't correspond to a known location, which the form surfaces rather than hides.
 */
export async function findRegionFor(province: string, city: string): Promise<string | null> {
  if (!province) return null
  const index = await loadIndex()

  for (const region of index.regions) {
    const provinceNames = index.provinces.get(region) ?? []
    const matchedProvince = provinceNames.find((p) => norm(p) === norm(province))
    if (!matchedProvince) continue
    if (!city) return region
    const cityNames = index.cities.get(`${region}|${matchedProvince}`) ?? []
    if (cityNames.some((c) => norm(c) === norm(city))) return region
  }
  return null
}
