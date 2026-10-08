import { formatDateTime, formatMetricValue } from '../../lib/format'
import { typeBadgeClass, typeLabel } from '../../lib/metricTypes'
import type { RoiData } from '../../types'

export default function CurrentFigures({ roi }: { roi: RoiData }) {
  // Computed metrics are the headline numbers, so they come first.
  const figures = [...roi.current].sort((a, b) => Number(b.kind === 'Computed') - Number(a.kind === 'Computed'))

  return (
    <section className="rounded-lg border border-gray-200 bg-white p-6">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-lg font-semibold">Current figures</h2>
        <p className="text-xs text-gray-500">
          {roi.asOf ? `As of ${formatDateTime(roi.asOf)} · reported by ${roi.reportedBy}` : 'No values reported yet'}
        </p>
      </div>

      <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {figures.map((f) => (
          <div key={f.metricDefinitionId} className="rounded-md border border-gray-200 p-3">
            <p className="flex items-center justify-between gap-2 text-xs text-gray-500">
              <span className="truncate">{f.label}</span>
              <span className={`shrink-0 rounded-full px-1.5 py-0.5 ${typeBadgeClass[f.valueType]}`}>
                {typeLabel(f.valueType, f.currencyCode)}
              </span>
            </p>
            {!f.hasValue ? (
              <p className="mt-1 text-sm text-gray-400">No value yet</p>
            ) : (
              <p
                className={`mt-1 text-xl font-semibold ${f.value === null ? 'text-gray-400' : ''}`}
                title={f.value === null ? 'Undefined for the latest data (for example, division by zero)' : undefined}
              >
                {formatMetricValue(f.valueType, f.value, f.currencyCode)}
              </p>
            )}
            {f.formula && <p className="mt-1 truncate font-mono text-xs text-gray-400" title={f.formula}>= {f.formula}</p>}
          </div>
        ))}
      </div>
    </section>
  )
}
