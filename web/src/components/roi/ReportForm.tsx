import { useState, type FormEvent } from 'react'
import { ApiError } from '../../api/client'
import { reportLog } from '../../api/logs'
import { typeLabel } from '../../lib/metricTypes'
import type { CurrentFigure } from '../../types'
import { inputClass } from '../AuthForm'

interface Props {
  automationId: string
  inputs: CurrentFigure[]
  onReported: () => void
}

type Hms = { h: string; m: string; s: string }

export default function ReportForm({ automationId, inputs, onReported }: Props) {
  const [plain, setPlain] = useState<Record<string, string>>({})
  const [durations, setDurations] = useState<Record<string, Hms>>({})
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)

  function touch() {
    setDone(false)
  }

  /** Converts the typed inputs to canonical units, or returns per-field errors. */
  function collect(): { values: { metricId: string; value: number }[]; errors: Record<string, string> } {
    const values: { metricId: string; value: number }[] = []
    const errs: Record<string, string> = {}

    for (const m of inputs) {
      const id = m.metricDefinitionId
      if (m.valueType === 'Duration') {
        const d = durations[id] ?? { h: '', m: '', s: '' }
        if (!d.h && !d.m && !d.s) {
          errs[id] = 'Enter a duration.'
          continue
        }
        const [h, mi, s] = [d.h, d.m, d.s].map((x) => (x === '' ? 0 : Number(x)))
        if (![h, mi, s].every((n) => Number.isFinite(n) && n >= 0)) {
          errs[id] = 'Use non-negative numbers.'
          continue
        }
        values.push({ metricId: id, value: h * 3600 + mi * 60 + s })
      } else {
        const raw = plain[id] ?? ''
        const n = Number(raw)
        if (raw.trim() === '' || !Number.isFinite(n)) errs[id] = 'Enter a number.'
        else values.push({ metricId: id, value: n })
      }
    }
    return { values, errors: errs }
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setFormError(null)
    setDone(false)

    const { values, errors: local } = collect()
    setErrors(local)
    if (Object.keys(local).length > 0) return

    setBusy(true)
    try {
      await reportLog(automationId, values)
      setPlain({})
      setDurations({})
      setDone(true)
      onReported()
    } catch (err) {
      if (err instanceof ApiError && Object.keys(err.fieldErrors).length > 0) {
        const byMetric: Record<string, string> = {}
        const general: string[] = []
        for (const [key, msgs] of Object.entries(err.fieldErrors)) {
          if (inputs.some((i) => i.metricDefinitionId === key)) byMetric[key] = msgs[0]
          else general.push(msgs[0])
        }
        setErrors(byMetric)
        if (general.length > 0) setFormError(general.join(' '))
      } else setFormError(err instanceof Error ? err.message : 'Something went wrong')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={onSubmit} className="rounded-lg border border-gray-200 bg-white p-6" noValidate>
      <h2 className="text-lg font-semibold">Report values</h2>
      <p className="mt-1 text-xs text-gray-500">
        Enter a value for every input metric. Computed metrics are calculated for you when you report.
      </p>

      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        {inputs.map((m) => {
          const id = m.metricDefinitionId
          const err = errors[id]
          const label = (
            <span className="flex items-center justify-between">
              {m.label}
              <span className="font-normal text-gray-400">{typeLabel(m.valueType, m.currencyCode)}</span>
            </span>
          )

          return (
            <div key={id} className="text-xs font-medium text-gray-600">
              {m.valueType === 'Duration' ? (
                <fieldset>
                  <legend className="w-full">{label}</legend>
                  <div className="mt-1 flex gap-2">
                    {(['h', 'm', 's'] as const).map((unit) => (
                      <label key={unit} className="flex flex-1 items-center gap-1">
                        <input
                          className={`${inputClass} mt-0`}
                          type="number"
                          min={0}
                          step="any"
                          inputMode="decimal"
                          aria-label={`${m.label} ${{ h: 'hours', m: 'minutes', s: 'seconds' }[unit]}`}
                          value={durations[id]?.[unit] ?? ''}
                          onChange={(e) => {
                            touch()
                            setDurations((d) => ({ ...d, [id]: { ...(d[id] ?? { h: '', m: '', s: '' }), [unit]: e.target.value } }))
                          }}
                        />
                        <span className="text-gray-400">{unit}</span>
                      </label>
                    ))}
                  </div>
                </fieldset>
              ) : (
                <label className="block">
                  {label}
                  <div className="relative mt-1">
                    {m.valueType === 'Currency' && (
                      <span className="pointer-events-none absolute left-3 top-2 text-sm text-gray-400">{m.currencyCode}</span>
                    )}
                    <input
                      className={`${inputClass} mt-0 ${m.valueType === 'Currency' ? 'pl-12' : ''} ${m.valueType === 'Percentage' ? 'pr-8' : ''}`}
                      type="number"
                      step="any"
                      inputMode="decimal"
                      value={plain[id] ?? ''}
                      onChange={(e) => {
                        touch()
                        setPlain((p) => ({ ...p, [id]: e.target.value }))
                      }}
                    />
                    {m.valueType === 'Percentage' && (
                      <span className="pointer-events-none absolute right-3 top-2 text-sm text-gray-400">%</span>
                    )}
                  </div>
                </label>
              )}
              {err && <span className="mt-1 block font-normal text-red-600">{err}</span>}
            </div>
          )
        })}
      </div>

      {formError && <p className="mt-3 text-sm text-red-600">{formError}</p>}
      <div className="mt-4 flex items-center gap-3">
        <button
          disabled={busy}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {busy ? 'Reporting...' : 'Report values'}
        </button>
        {done && <span className="text-sm text-green-700">Reported. Figures, chart and history are updated.</span>}
      </div>
    </form>
  )
}
