import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { getRoi } from '../../api/logs'
import { useRoiLongPoll, type LiveStatus } from '../../hooks/useRoiLongPoll'
import { formatDate } from '../../lib/format'
import type { RoiData } from '../../types'
import MetricsPanel from '../metrics/MetricsPanel'
import CurrentFigures from './CurrentFigures'
import LogHistory from './LogHistory'
import ReportForm from './ReportForm'
import TrendChart from './TrendChart'

const statusText: Record<LiveStatus, string> = {
  connecting: 'Connecting...',
  live: 'Live: changes appear automatically',
  reconnecting: 'Reconnecting...',
}
const statusDot: Record<LiveStatus, string> = {
  connecting: 'bg-gray-300',
  live: 'bg-green-500',
  reconnecting: 'bg-amber-500',
}

export default function RoiTab({ automationId, canEdit }: { automationId: string; canEdit: boolean }) {
  const [roi, setRoi] = useState<RoiData | null>(null)
  const [error, setError] = useState<string | null>(null)
  // refreshKey re-fetches the ROI payload (after this user's own writes). listsKey reloads the history and metrics lists
  // and changes for both own writes and changes pushed in by the long poll.
  const [refreshKey, setRefreshKey] = useState(0)
  const [listsKey, setListsKey] = useState(0)
  const versionRef = useRef<number | null>(null)

  const refresh = () => {
    setRefreshKey((k) => k + 1)
    setListsKey((k) => k + 1)
  }

  useEffect(() => {
    const ctrl = new AbortController()
    getRoi(automationId, ctrl.signal)
      .then((r) => {
        versionRef.current = r.dataVersion
        setRoi(r)
        setError(null)
      })
      .catch((e: Error) => e.name !== 'AbortError' && setError(e.message))
    return () => ctrl.abort()
  }, [automationId, refreshKey])

  // Someone else (or another tab) changed the data: the long poll hands us the new payload directly.
  const onRemoteChange = useCallback((r: RoiData) => {
    if (r.dataVersion === versionRef.current) return // already showing it (our own write)
    versionRef.current = r.dataVersion
    setRoi(r)
    setListsKey((k) => k + 1)
  }, [])
  const live = useRoiLongPoll(automationId, () => versionRef.current, onRemoteChange)

  const liveIds = useMemo(() => new Set(roi?.current.map((c) => c.metricDefinitionId)), [roi])
  const inputs = useMemo(() => roi?.current.filter((c) => c.kind === 'Input') ?? [], [roi])

  if (error) return <p className="mt-4 text-red-600">Could not load ROI data: {error}</p>
  if (!roi) return <p className="mt-4 text-gray-500">Loading...</p>

  const hasMetrics = roi.current.length > 0

  return (
    <div className="mt-4 space-y-4">
      <p className="flex items-center justify-end gap-2 text-xs text-gray-500" aria-live="polite">
        <span className={`h-2 w-2 rounded-full ${statusDot[live]}`} />
        {statusText[live]}
      </p>

      {roi.isStale && roi.asOf && (
        <div role="status" className="rounded-md border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
          No report since {formatDate(roi.asOf)}. These figures may be out of date.
        </div>
      )}

      {hasMetrics && <CurrentFigures roi={roi} />}
      {hasMetrics && <TrendChart series={roi.series} />}
      {canEdit && inputs.length > 0 && (
        <ReportForm key={inputs.map((i) => i.metricDefinitionId).join(',')} automationId={automationId} inputs={inputs} onReported={refresh} />
      )}
      {hasMetrics && <LogHistory automationId={automationId} refreshKey={listsKey} liveIds={liveIds} />}
      <MetricsPanel automationId={automationId} canEdit={canEdit} reloadKey={listsKey} onChanged={refresh} />
    </div>
  )
}
