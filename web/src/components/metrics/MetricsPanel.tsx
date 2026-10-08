import { useCallback, useEffect, useState } from 'react'
import { deleteMetric, listMetrics } from '../../api/metrics'
import { errorMessage, typeBadgeClass, typeLabelOf } from '../../lib/metricTypes'
import type { MetricDefinition } from '../../types'
import AddComputedForm from './AddComputedForm'
import AddInputForm from './AddInputForm'

export default function MetricsPanel({ automationId, canEdit }: { automationId: string; canEdit: boolean }) {
  const [metrics, setMetrics] = useState<MetricDefinition[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  const reload = useCallback(
    (signal?: AbortSignal) =>
      listMetrics(automationId, signal)
        .then((m) => {
          setMetrics(m)
          setLoadError(null)
        })
        .catch((e: Error) => e.name !== 'AbortError' && setLoadError(e.message)),
    [automationId],
  )

  useEffect(() => {
    const ctrl = new AbortController()
    reload(ctrl.signal)
    return () => ctrl.abort()
  }, [reload])

  async function onDelete(m: MetricDefinition) {
    const consequence =
      m.kind === 'Computed'
        ? 'It will no longer be calculated for new reports; existing logs keep their recorded values.'
        : 'It will no longer be reported going forward; existing logs keep their recorded values.'
    if (!window.confirm(`Delete "${m.label}"? ${consequence}`)) return
    setActionError(null)
    try {
      await deleteMetric(automationId, m.id)
      await reload()
    } catch (e) {
      setActionError(errorMessage(e))
    }
  }

  const inputs = (metrics ?? []).filter((m) => m.kind === 'Input')

  return (
    <section className="mt-4 space-y-4">
      <div className="rounded-lg border border-gray-200 bg-white p-6">
        <h2 className="text-lg font-semibold">Metrics</h2>

        {loadError && <p className="mt-3 text-sm text-red-600">Could not load metrics: {loadError}</p>}
        {!loadError && metrics === null && <p className="mt-3 text-sm text-gray-500">Loading...</p>}
        {actionError && <p className="mt-3 text-sm text-red-600">{actionError}</p>}

        {metrics?.length === 0 && (
          <div className="mt-4 rounded-md border border-dashed border-gray-300 p-6 text-center">
            <p className="font-medium text-gray-700">No metrics defined yet</p>
            <p className="mt-1 text-sm text-gray-500">
              {canEdit
                ? 'Add input metrics below, then optionally a computed metric built from them.'
                : 'A Technical team member needs to define metrics for this automation.'}
            </p>
          </div>
        )}

        {metrics && metrics.length > 0 && (
          <ul className="mt-4 divide-y divide-gray-100">
            {metrics.map((m) => (
              <li key={m.id} className="flex flex-wrap items-start justify-between gap-2 py-3">
                <div className="min-w-0">
                  <p className="flex flex-wrap items-center gap-2 text-sm font-medium">
                    {m.label}
                    <span className={`rounded-full px-2 py-0.5 text-xs ${typeBadgeClass[m.valueType]}`}>{typeLabelOf(m)}</span>
                    <span className="text-xs font-normal text-gray-400">{m.kind === 'Input' ? 'input' : 'computed'}</span>
                  </p>
                  {m.formulaText && <p className="mt-1 break-all font-mono text-xs text-gray-600">= {m.formulaText}</p>}
                </div>
                {canEdit && (
                  <button onClick={() => onDelete(m)} className="text-sm text-red-600 hover:underline">
                    Delete
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>

      {canEdit && metrics && (
        <div className="grid gap-4 lg:grid-cols-2">
          <AddInputForm automationId={automationId} onCreated={() => reload()} />
          <AddComputedForm automationId={automationId} inputs={inputs} onCreated={() => reload()} />
        </div>
      )}
    </section>
  )
}
