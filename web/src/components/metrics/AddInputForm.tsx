import { useState, type FormEvent } from 'react'
import { createInputMetric } from '../../api/metrics'
import { errorMessage, VALUE_TYPES } from '../../lib/metricTypes'
import type { MetricValueType } from '../../types'
import { inputClass } from '../AuthForm'

export default function AddInputForm({ automationId, onCreated }: { automationId: string; onCreated: () => void }) {
  const [label, setLabel] = useState('')
  const [type, setType] = useState<MetricValueType>('Number')
  const [currency, setCurrency] = useState('USD')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await createInputMetric(automationId, label, type, currency)
      setLabel('')
      onCreated()
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={onSubmit} className="rounded-md border border-gray-200 p-4">
      <h3 className="text-sm font-semibold">Add input metric</h3>
      <div className="mt-3 grid gap-3 sm:grid-cols-3">
        <label className="text-xs font-medium text-gray-600 sm:col-span-3">
          Label
          <input
            className={inputClass}
            placeholder="e.g. Records processed"
            value={label}
            onChange={(e) => setLabel(e.target.value)}
            maxLength={100}
            required
          />
        </label>
        <label className="text-xs font-medium text-gray-600">
          Type
          <select className={inputClass} value={type} onChange={(e) => setType(e.target.value as MetricValueType)}>
            {VALUE_TYPES.map((t) => (
              <option key={t}>{t}</option>
            ))}
          </select>
        </label>
        {type === 'Currency' && (
          <label className="text-xs font-medium text-gray-600">
            Currency code
            <input
              className={inputClass}
              value={currency}
              onChange={(e) => setCurrency(e.target.value.toUpperCase())}
              maxLength={3}
              required
            />
          </label>
        )}
      </div>
      {error && <p className="mt-2 text-sm text-red-600">{error}</p>}
      <button
        disabled={busy}
        className="mt-3 rounded-md bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
      >
        {busy ? 'Adding...' : 'Add input'}
      </button>
    </form>
  )
}
