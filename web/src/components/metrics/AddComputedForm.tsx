import { useEffect, useRef, useState, type FormEvent } from 'react'
import { createComputedMetric, validateFormula } from '../../api/metrics'
import { useDebouncedValue } from '../../hooks/useDebouncedValue'
import { errorMessage, typeLabel } from '../../lib/metricTypes'
import type { MetricDefinition, ValidateFormulaResult } from '../../types'
import { inputClass } from '../AuthForm'

interface Props {
  automationId: string
  inputs: MetricDefinition[]
  onCreated: () => void
}

type Check = { state: 'idle' } | { state: 'checking' } | { state: 'done'; result: ValidateFormulaResult } | { state: 'failed'; message: string }

export default function AddComputedForm({ automationId, inputs, onCreated }: Props) {
  const [label, setLabel] = useState('')
  const [formula, setFormula] = useState('')
  const [check, setCheck] = useState<Check>({ state: 'idle' })
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const areaRef = useRef<HTMLTextAreaElement>(null)

  const debounced = useDebouncedValue(formula, 400)
  // The input list changes when metrics are added or deleted; re-validate against the new list.
  const inputsKey = inputs.map((i) => i.id).join(',')

  useEffect(() => {
    if (!debounced.trim()) {
      setCheck({ state: 'idle' })
      return
    }
    const ctrl = new AbortController()
    setCheck({ state: 'checking' })
    validateFormula(automationId, debounced, ctrl.signal)
      .then((result) => setCheck({ state: 'done', result }))
      .catch((e: Error) => e.name !== 'AbortError' && setCheck({ state: 'failed', message: e.message }))
    return () => ctrl.abort()
  }, [automationId, debounced, inputsKey])

  function insertRef(labelText: string) {
    const el = areaRef.current
    const token = `[${labelText}]`
    if (!el) return setFormula((f) => f + token)
    const start = el.selectionStart
    const end = el.selectionEnd
    setFormula(formula.slice(0, start) + token + formula.slice(end))
    requestAnimationFrame(() => {
      el.focus()
      el.setSelectionRange(start + token.length, start + token.length)
    })
  }

  // Only trust the check if it matches what is currently typed.
  const current = check.state === 'done' && debounced === formula ? check.result : null
  const canSave = !busy && label.trim() !== '' && current?.valid === true

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await createComputedMetric(automationId, label, formula)
      setLabel('')
      setFormula('')
      setCheck({ state: 'idle' })
      onCreated()
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={onSubmit} className="rounded-md border border-gray-200 p-4">
      <h3 className="text-sm font-semibold">Add computed metric</h3>
      <p className="mt-1 text-xs text-gray-500">
        Combine input metrics with + - * / and parentheses. Formulas cannot be edited later: delete and recreate to change one.
      </p>

      <label className="mt-3 block text-xs font-medium text-gray-600">
        Label
        <input
          className={inputClass}
          placeholder="e.g. Time saved"
          value={label}
          onChange={(e) => setLabel(e.target.value)}
          maxLength={100}
          required
        />
      </label>

      <label className="mt-3 block text-xs font-medium text-gray-600">
        Formula
        <textarea
          ref={areaRef}
          className={`${inputClass} font-mono`}
          rows={2}
          placeholder="[Manual time per run] - [Automated time per run]"
          value={formula}
          onChange={(e) => setFormula(e.target.value)}
          required
          spellCheck={false}
        />
      </label>

      <div className="mt-2 flex flex-wrap items-center gap-1.5">
        <span className="text-xs text-gray-500">Insert:</span>
        {inputs.length === 0 && <span className="text-xs text-gray-400">add input metrics first</span>}
        {inputs.map((i) => (
          <button
            key={i.id}
            type="button"
            onClick={() => insertRef(i.label)}
            className="rounded-full border border-gray-300 px-2 py-0.5 text-xs hover:bg-gray-50"
            title={typeLabel(i.valueType, i.currencyCode)}
          >
            {i.label}
          </button>
        ))}
      </div>

      <div className="mt-3 min-h-[1.5rem] text-sm" aria-live="polite">
        {check.state === 'checking' && <span className="text-gray-500">Checking...</span>}
        {check.state === 'failed' && <span className="text-red-600">Could not validate: {check.message}</span>}
        {current?.valid && (
          <span className="text-green-700">
            Valid. Result type: <strong>{typeLabel(current.resultType!, current.currencyCode)}</strong>
          </span>
        )}
        {current && !current.valid && (
          <ul className="space-y-0.5 text-red-600">
            {current.errors.map((e, i) => (
              <li key={i}>
                {e.message} <span className="text-xs text-red-400">(position {e.position})</span>
              </li>
            ))}
          </ul>
        )}
      </div>

      {error && <p className="mt-2 text-sm text-red-600">{error}</p>}
      <button
        disabled={!canSave}
        className="mt-3 rounded-md bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
      >
        {busy ? 'Adding...' : 'Add computed metric'}
      </button>
    </form>
  )
}
