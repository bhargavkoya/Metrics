import type { MetricDefinition, MetricValueType, ValidateFormulaResult } from '../types'
import { api } from './client'

const base = (automationId: string) => `/automations/${automationId}/metrics`

export const listMetrics = (automationId: string, signal?: AbortSignal) =>
  api<MetricDefinition[]>(base(automationId), { signal })

export const createInputMetric = (
  automationId: string,
  label: string,
  valueType: MetricValueType,
  currencyCode?: string,
) =>
  api<MetricDefinition>(base(automationId), {
    method: 'POST',
    json: { label, kind: 'Input', valueType, currencyCode: valueType === 'Currency' ? currencyCode : undefined },
  })

export const createComputedMetric = (automationId: string, label: string, formula: string) =>
  api<MetricDefinition>(base(automationId), { method: 'POST', json: { label, kind: 'Computed', formula } })

export const validateFormula = (automationId: string, formula: string, signal?: AbortSignal) =>
  api<ValidateFormulaResult>(`${base(automationId)}/validate-formula`, { method: 'POST', json: { formula }, signal })

export const deleteMetric = (automationId: string, metricId: string) =>
  api<void>(`${base(automationId)}/${metricId}`, { method: 'DELETE' })
