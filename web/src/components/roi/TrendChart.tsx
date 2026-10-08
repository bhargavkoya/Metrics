import { useMemo, useState } from 'react'
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { formatAxisValue, formatDateTime, formatMetricValue } from '../../lib/format'
import type { Series } from '../../types'
import { inputClass } from '../AuthForm'

const shortDate = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' })

export default function TrendChart({ series }: { series: Series[] }) {
  // Computed metrics first: they are the ROI story. Only live metrics are charted; history lists everything.
  const ordered = useMemo(
    () => [...series].sort((a, b) => Number(b.kind === 'Computed') - Number(a.kind === 'Computed')),
    [series],
  )
  const [chosen, setChosen] = useState<string | null>(null)
  const selected = ordered.find((s) => s.metricDefinitionId === chosen) ?? ordered.find((s) => s.points.length > 0) ?? ordered[0]

  if (!selected) return null

  const data = selected.points.map((p) => ({ time: new Date(p.reportedAt).getTime(), value: p.value }))

  return (
    <section className="rounded-lg border border-gray-200 bg-white p-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold">Trend</h2>
        <label className="flex items-center gap-2 text-xs font-medium text-gray-600">
          Metric
          <select
            className={`${inputClass} mt-0 w-auto`}
            value={selected.metricDefinitionId}
            onChange={(e) => setChosen(e.target.value)}
          >
            {ordered.map((s) => (
              <option key={s.metricDefinitionId} value={s.metricDefinitionId}>
                {s.label}
              </option>
            ))}
          </select>
        </label>
      </div>

      {data.length < 2 ? (
        <p className="mt-4 rounded-md border border-dashed border-gray-300 p-6 text-center text-sm text-gray-500">
          {data.length === 0
            ? 'No values reported for this metric yet.'
            : 'Report at least two sets of values to see a trend.'}
        </p>
      ) : (
        <div className="mt-4 h-72" role="img" aria-label={`Trend of ${selected.label} over time`}>
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={data} margin={{ top: 8, right: 16, bottom: 0, left: 8 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
              <XAxis
                dataKey="time"
                type="number"
                scale="time"
                domain={['dataMin', 'dataMax']}
                tickFormatter={(t: number) => shortDate.format(new Date(t))}
                tick={{ fontSize: 12 }}
              />
              <YAxis
                width={72}
                tick={{ fontSize: 12 }}
                tickFormatter={(v: number) => formatAxisValue(selected.valueType, v, selected.currencyCode)}
              />
              <Tooltip
                labelFormatter={(t) => formatDateTime(new Date(Number(t)).toISOString())}
                formatter={(v) => [
                  formatMetricValue(selected.valueType, typeof v === 'number' ? v : null, selected.currencyCode),
                  selected.label,
                ]}
              />
              <Line type="monotone" dataKey="value" stroke="#2563eb" strokeWidth={2} dot={{ r: 3 }} connectNulls={false} isAnimationActive={false} />
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}
    </section>
  )
}
