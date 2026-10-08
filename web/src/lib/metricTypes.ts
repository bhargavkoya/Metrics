import type { MetricDefinition, MetricValueType } from '../types'

export const VALUE_TYPES: MetricValueType[] = ['Number', 'Percentage', 'Currency', 'Duration']

export function typeLabel(valueType: MetricValueType, currencyCode: string | null): string {
  return valueType === 'Currency' && currencyCode ? `Currency (${currencyCode})` : valueType
}

export const typeLabelOf = (m: Pick<MetricDefinition, 'valueType' | 'currencyCode'>) =>
  typeLabel(m.valueType, m.currencyCode)

export const typeBadgeClass: Record<MetricValueType, string> = {
  Number: 'bg-slate-100 text-slate-700',
  Percentage: 'bg-purple-100 text-purple-700',
  Currency: 'bg-emerald-100 text-emerald-700',
  Duration: 'bg-sky-100 text-sky-700',
}

/** Server messages for field errors (400) or conflicts (409), flattened to one line. */
export function errorMessage(e: unknown): string {
  if (e instanceof Error && 'fieldErrors' in e) {
    const fe = (e as { fieldErrors: Record<string, string[]> }).fieldErrors
    const all = Object.values(fe).flat()
    if (all.length > 0) return all.join(' ')
  }
  return e instanceof Error ? e.message : 'Something went wrong'
}
