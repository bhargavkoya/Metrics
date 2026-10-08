import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { listAutomations, listDepartments } from '../api/automations'
import AutomationCard from '../components/AutomationCard'
import { inputClass } from '../components/AuthForm'
import { useDebouncedValue } from '../hooks/useDebouncedValue'
import type { AutomationCard as Card } from '../types'

// Filters live in the URL so a filtered view survives refresh and can be shared.
export default function CatalogPage() {
  const [params, setParams] = useSearchParams()
  const q = params.get('q') ?? ''
  const department = params.get('department') ?? ''
  const from = params.get('from') ?? ''
  const to = params.get('to') ?? ''

  const [search, setSearch] = useState(q)
  const debouncedSearch = useDebouncedValue(search, 300)
  const [departments, setDepartments] = useState<string[]>([])
  const [items, setItems] = useState<Card[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const rangeInvalid = !!from && !!to && from > to

  function setParam(key: string, value: string) {
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (value) next.set(key, value)
        else next.delete(key)
        return next
      },
      { replace: true },
    )
  }

  // Push the debounced search text into the URL.
  useEffect(() => {
    if (debouncedSearch !== q) setParam('q', debouncedSearch.trim())
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debouncedSearch])

  useEffect(() => {
    const ctrl = new AbortController()
    listDepartments(ctrl.signal).then(setDepartments).catch(() => {})
    return () => ctrl.abort()
  }, [])

  useEffect(() => {
    if (rangeInvalid) return
    const ctrl = new AbortController()
    listAutomations({ q, department, from, to }, ctrl.signal)
      .then((r) => {
        setItems(r)
        setError(null)
      })
      .catch((e: Error) => e.name !== 'AbortError' && setError(e.message))
    return () => ctrl.abort()
  }, [q, department, from, to, rangeInvalid])

  const hasFilters = !!(q || department || from || to)

  function clear() {
    setSearch('')
    setParams({}, { replace: true })
  }

  return (
    <div>
      <h1 className="text-xl font-semibold">Automations</h1>

      <div className="mt-4 grid gap-3 rounded-lg border border-gray-200 bg-white p-4 sm:grid-cols-2 lg:grid-cols-5">
        <label className="text-xs font-medium text-gray-600 lg:col-span-2">
          Search
          <input
            className={inputClass}
            placeholder="Name or description"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </label>
        <label className="text-xs font-medium text-gray-600">
          Department
          <select className={inputClass} value={department} onChange={(e) => setParam('department', e.target.value)}>
            <option value="">All</option>
            {departments.map((d) => (
              <option key={d} value={d}>
                {d}
              </option>
            ))}
          </select>
        </label>
        <label className="text-xs font-medium text-gray-600">
          Active from
          <input className={inputClass} type="date" value={from} onChange={(e) => setParam('from', e.target.value)} />
        </label>
        <label className="text-xs font-medium text-gray-600">
          Active to
          <input className={inputClass} type="date" value={to} onChange={(e) => setParam('to', e.target.value)} />
        </label>
      </div>

      <div className="mt-2 flex items-center justify-between text-sm">
        <span className="text-red-600">{rangeInvalid && "'Active to' must be on or after 'Active from'."}</span>
        {hasFilters && (
          <button onClick={clear} className="text-blue-600 hover:underline">
            Clear filters
          </button>
        )}
      </div>

      <div className="mt-4">
        {error && <p className="text-red-600">Could not load automations: {error}</p>}
        {!error && items === null && <p className="text-gray-500">Loading...</p>}
        {!error && items?.length === 0 && (
          <p className="rounded-lg border border-dashed border-gray-300 p-8 text-center text-gray-500">
            No automations match these filters.
          </p>
        )}
        {items && items.length > 0 && (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {items.map((a) => (
              <AutomationCard key={a.id} automation={a} />
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
