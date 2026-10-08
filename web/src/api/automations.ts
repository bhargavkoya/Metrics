import type { AutomationCard, AutomationDetail } from '../types'
import { api } from './client'

export interface CatalogFilters {
  q: string
  department: string
  from: string
  to: string
}

export function listAutomations(f: CatalogFilters, signal?: AbortSignal) {
  const params = new URLSearchParams()
  if (f.q) params.set('q', f.q)
  if (f.department) params.set('department', f.department)
  if (f.from) params.set('from', f.from)
  if (f.to) params.set('to', f.to)
  const qs = params.toString()
  return api<AutomationCard[]>(`/automations${qs ? `?${qs}` : ''}`, { signal })
}

export const listDepartments = (signal?: AbortSignal) => api<string[]>('/automations/departments', { signal })

export const getAutomation = (id: string, signal?: AbortSignal) =>
  api<AutomationDetail>(`/automations/${id}`, { signal })
