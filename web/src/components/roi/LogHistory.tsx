import { useCallback, useEffect, useState } from 'react'
import { listLogs } from '../../api/logs'
import { formatDateTime, formatMetricValue } from '../../lib/format'
import type { LogEntry } from '../../types'

const PAGE_SIZE = 10

interface Props {
  automationId: string
  /** Changes whenever data may have changed; the list reloads from the first page. */
  refreshKey: number
  /** Metric ids that still exist. Values for other ids stay visible but are marked as removed. */
  liveIds: Set<string>
}

export default function LogHistory({ automationId, refreshKey, liveIds }: Props) {
  const [logs, setLogs] = useState<LogEntry[] | null>(null)
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [error, setError] = useState<string | null>(null)
  const [loadingMore, setLoadingMore] = useState(false)

  useEffect(() => {
    const ctrl = new AbortController()
    listLogs(automationId, 1, PAGE_SIZE, ctrl.signal)
      .then((r) => {
        setLogs(r.items)
        setTotal(r.total)
        setPage(1)
        setError(null)
      })
      .catch((e: Error) => e.name !== 'AbortError' && setError(e.message))
    return () => ctrl.abort()
  }, [automationId, refreshKey])

  const loadMore = useCallback(async () => {
    setLoadingMore(true)
    try {
      const r = await listLogs(automationId, page + 1, PAGE_SIZE)
      setLogs((prev) => [...(prev ?? []), ...r.items])
      setTotal(r.total)
      setPage(r.page)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load more')
    } finally {
      setLoadingMore(false)
    }
  }, [automationId, page])

  return (
    <section className="rounded-lg border border-gray-200 bg-white p-6">
      <h2 className="text-lg font-semibold">History</h2>

      {error && <p className="mt-3 text-sm text-red-600">Could not load history: {error}</p>}
      {!error && logs === null && <p className="mt-3 text-sm text-gray-500">Loading...</p>}
      {logs?.length === 0 && <p className="mt-3 text-sm text-gray-500">Nothing has been reported yet.</p>}

      {logs && logs.length > 0 && (
        <>
          <ul className="mt-4 divide-y divide-gray-100">
            {logs.map((log) => (
              <li key={log.id} className="py-3">
                <p className="text-sm font-medium">
                  {formatDateTime(log.reportedAt)} <span className="font-normal text-gray-500">· {log.reportedBy}</span>
                </p>
                <ul className="mt-2 flex flex-wrap gap-1.5">
                  {log.values.map((v) => {
                    const removed = !liveIds.has(v.metricDefinitionId)
                    return (
                      <li
                        key={v.metricDefinitionId}
                        title={v.formula ? `Calculated at report time: ${v.formula}` : undefined}
                        className={`rounded-md px-2 py-1 text-xs ${
                          v.role === 'Computed' ? 'bg-blue-50 text-blue-800' : 'bg-gray-100 text-gray-700'
                        } ${removed ? 'opacity-60' : ''}`}
                      >
                        {v.label}: <strong>{formatMetricValue(v.valueType, v.value, v.currencyCode)}</strong>
                        {removed && <span className="ml-1 italic">(removed)</span>}
                      </li>
                    )
                  })}
                </ul>
              </li>
            ))}
          </ul>

          <div className="mt-3 flex items-center justify-between text-sm text-gray-500">
            <span>
              Showing {logs.length} of {total}
            </span>
            {logs.length < total && (
              <button onClick={loadMore} disabled={loadingMore} className="text-blue-600 hover:underline disabled:opacity-50">
                {loadingMore ? 'Loading...' : 'Load more'}
              </button>
            )}
          </div>
        </>
      )}
    </section>
  )
}
