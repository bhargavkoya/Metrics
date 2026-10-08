import { useEffect, useMemo, useState } from 'react'
import { getRoi } from '../../api/logs'
import type { RoiData } from '../../types'
import MetricsPanel from '../metrics/MetricsPanel'
import CurrentFigures from './CurrentFigures'
import LogHistory from './LogHistory'
import ReportForm from './ReportForm'
import TrendChart from './TrendChart'

export default function RoiTab({ automationId, canEdit }: { automationId: string; canEdit: boolean }) {
  const [roi, setRoi] = useState<RoiData | null>(null)
  const [error, setError] = useState<string | null>(null)
  // Bumped after any write (report, metric added or deleted) so every section reloads.
  const [refreshKey, setRefreshKey] = useState(0)
  const refresh = () => setRefreshKey((k) => k + 1)

  useEffect(() => {
    const ctrl = new AbortController()
    getRoi(automationId, ctrl.signal)
      .then((r) => {
        setRoi(r)
        setError(null)
      })
      .catch((e: Error) => e.name !== 'AbortError' && setError(e.message))
    return () => ctrl.abort()
  }, [automationId, refreshKey])

  const liveIds = useMemo(() => new Set(roi?.current.map((c) => c.metricDefinitionId)), [roi])
  const inputs = useMemo(() => roi?.current.filter((c) => c.kind === 'Input') ?? [], [roi])

  if (error) return <p className="mt-4 text-red-600">Could not load ROI data: {error}</p>
  if (!roi) return <p className="mt-4 text-gray-500">Loading...</p>

  const hasMetrics = roi.current.length > 0

  return (
    <div className="mt-4 space-y-4">
      {hasMetrics && <CurrentFigures roi={roi} />}
      {hasMetrics && <TrendChart series={roi.series} />}
      {canEdit && inputs.length > 0 && (
        <ReportForm key={inputs.map((i) => i.metricDefinitionId).join(',')} automationId={automationId} inputs={inputs} onReported={refresh} />
      )}
      {hasMetrics && <LogHistory automationId={automationId} refreshKey={refreshKey} liveIds={liveIds} />}
      <MetricsPanel automationId={automationId} canEdit={canEdit} onChanged={refresh} />
    </div>
  )
}
