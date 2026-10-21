import { LuPlus, LuSquarePen, LuTrash2 } from 'react-icons/lu'
import type {
  CityItem,
  ProvinceItem,
  RegionItem,
} from '../../../core/api/endpoints/locationsApi'
import { StandardButton } from '../components/shared/StandardButton'
import type { LocationKind } from './LocationFormModal'

type Row = RegionItem | ProvinceItem | CityItem

interface LocationsTableProps {
  kind: LocationKind
  rows: Row[]
  loading: boolean
  searchInput: string
  onSearchInputChange: (value: string) => void
  regions: RegionItem[]
  regionFilter: string
  onRegionFilterChange: (value: string) => void
  /** Provinces of the selected region, for the Cities tab's province filter. */
  provinceOptions: ProvinceItem[]
  provinceFilter: string
  onProvinceFilterChange: (value: string) => void
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
  onAdd: () => void
  onEdit: (row: Row) => void
  onDelete: (row: Row) => void
}

const NOUN: Record<LocationKind, string> = {
  regions: 'region',
  provinces: 'province',
  cities: 'city or municipality',
}

export const LocationsTable = ({
  kind,
  rows,
  loading,
  searchInput,
  onSearchInputChange,
  regions,
  regionFilter,
  onRegionFilterChange,
  provinceOptions,
  provinceFilter,
  onProvinceFilterChange,
  page,
  pageSize,
  totalCount,
  onPageChange,
  onAdd,
  onEdit,
  onDelete,
}: LocationsTableProps) => {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const headers =
    kind === 'regions'
      ? ['Region', 'Provinces']
      : kind === 'provinces'
        ? ['Province', 'Region', 'Cities / municipalities']
        : ['City / municipality', 'Province', 'Region', 'Zip code']
  const columnCount = headers.length + 1

  return (
    <div className="card">
      <div className="flex flex-wrap items-center gap-3 px-5 py-3 border-b border-default-200 bg-default-50">
        <input
          type="text"
          className="form-input max-w-xs"
          placeholder={kind === 'cities' ? 'Search name or zip code…' : 'Search by name…'}
          aria-label={`Search ${NOUN[kind]}`}
          value={searchInput}
          onChange={(e) => onSearchInputChange(e.target.value)}
        />
        {kind !== 'regions' && (
          <select
            className="form-input max-w-xs"
            aria-label="Filter by region"
            value={regionFilter}
            onChange={(e) => onRegionFilterChange(e.target.value)}
          >
            <option value="">All regions</option>
            {regions.map((r) => (
              <option key={r.id} value={r.id}>
                {r.name}
              </option>
            ))}
          </select>
        )}
        {kind === 'cities' && (
          <select
            className="form-input max-w-xs"
            aria-label="Filter by province"
            value={provinceFilter}
            disabled={!regionFilter}
            onChange={(e) => onProvinceFilterChange(e.target.value)}
          >
            <option value="">{regionFilter ? 'All provinces' : 'Choose a region to filter provinces'}</option>
            {provinceOptions.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        )}
        <div className="ms-auto">
          <StandardButton size="sm" variant="on-primary" icon={LuPlus} onClick={onAdd}>
            Add {NOUN[kind]}
          </StandardButton>
        </div>
      </div>

      <div className="overflow-x-auto">
        <table className="min-w-full divide-y divide-default-200">
          <thead className="bg-default-150">
            <tr className="text-sm font-normal text-default-700 whitespace-nowrap">
              {headers.map((h) => (
                <th key={h} className="px-3.5 py-3 text-start">
                  {h}
                </th>
              ))}
              <th className="px-3.5 py-3 text-end">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-default-200">
            {rows.map((row) => (
              <tr key={row.id} className="text-default-800 font-normal text-sm whitespace-nowrap">
                {kind === 'regions' && (
                  <>
                    <td className="py-3 px-3.5">{row.name}</td>
                    <td className="py-3 px-3.5">{(row as RegionItem).provinceCount}</td>
                  </>
                )}
                {kind === 'provinces' && (
                  <>
                    <td className="py-3 px-3.5">{row.name}</td>
                    <td className="py-3 px-3.5">{(row as ProvinceItem).regionName}</td>
                    <td className="py-3 px-3.5">{(row as ProvinceItem).cityCount}</td>
                  </>
                )}
                {kind === 'cities' && (
                  <>
                    <td className="py-3 px-3.5">{row.name}</td>
                    <td className="py-3 px-3.5">{(row as CityItem).provinceName}</td>
                    <td className="py-3 px-3.5">{(row as CityItem).regionName}</td>
                    <td className="py-3 px-3.5">{(row as CityItem).zipCode ?? '—'}</td>
                  </>
                )}
                <td className="py-3 px-3.5 text-end">
                  <div className="inline-flex items-center gap-3">
                    <button
                      type="button"
                      className="text-primary hover:underline"
                      aria-label={`Edit ${row.name}`}
                      onClick={() => onEdit(row)}
                    >
                      <LuSquarePen className="size-4" />
                    </button>
                    <button
                      type="button"
                      className="text-danger hover:underline"
                      aria-label={`Delete ${row.name}`}
                      onClick={() => onDelete(row)}
                    >
                      <LuTrash2 className="size-4" />
                    </button>
                  </div>
                </td>
              </tr>
            ))}
            {!loading && rows.length === 0 && (
              <tr>
                <td colSpan={columnCount} className="py-6 px-3.5 text-center text-default-500">
                  No {NOUN[kind]} entries match this filter.
                </td>
              </tr>
            )}
            {loading && rows.length === 0 && (
              <tr>
                <td colSpan={columnCount} className="py-6 px-3.5 text-center text-default-500">
                  Loading…
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {kind !== 'regions' && (
        <div className="card-footer flex items-center justify-between">
          <span className="text-sm text-default-500">
            Page {page} of {totalPages} ({totalCount} total)
          </span>
          <div className="flex items-center gap-1.5">
            <button
              type="button"
              className="btn btn-sm border border-default-200 disabled:opacity-50"
              disabled={page <= 1}
              onClick={() => onPageChange(page - 1)}
            >
              Previous
            </button>
            <button
              type="button"
              className="btn btn-sm border border-default-200 disabled:opacity-50"
              disabled={page >= totalPages}
              onClick={() => onPageChange(page + 1)}
            >
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
