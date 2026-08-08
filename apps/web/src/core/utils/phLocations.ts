export interface PhLocationRow {
  region: string
  province: string
  city: string
  /** Null for 76 of 1,647 entries - mostly newer/BARMM-area municipalities with no
   *  well-documented ZIP. Expected data, not a defect: the UI leaves ZIP blank and editable. */
  zip_code: string | null
}

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
 * Loads and indexes the bundled PSGC-derived location data.
 *
 * Dynamic import, not a static one, so the ~168KB dataset is a separate chunk fetched only when an
 * address form actually renders - an admin browsing /members never pays for it. The promise is
 * cached, so concurrent callers (residence + mailing rendering together) share one fetch.
 */
function loadIndex(): Promise<PhLocationIndex> {
  indexPromise ??= (async () => {
    const [locationsModule, countriesModule] = await Promise.all([
      import('../../data/ph-locations.json'),
      import('../../data/countries.json'),
    ])
    const rows = locationsModule.default as PhLocationRow[]
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
      zips.set(`${provinceKey}|${row.city}`, row.zip_code)
    }

    return { regions, provinces, cities, zips, countries }
  })()
  return indexPromise
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

export async function getCities(region: string, province: string): Promise<string[]> {
  if (!region || !province) return []
  return (await loadIndex()).cities.get(`${region}|${province}`) ?? []
}

/** Null both when the city genuinely has no mapped ZIP and when the city isn't found at all -
 *  callers treat both the same way (leave whatever the user typed alone). */
export async function getZipCode(region: string, province: string, city: string): Promise<string | null> {
  if (!region || !province || !city) return null
  return (await loadIndex()).zips.get(`${region}|${province}|${city}`) ?? null
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
  const norm = (v: string) => v.trim().toLowerCase()

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
