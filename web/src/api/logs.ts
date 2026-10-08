import type { LogEntry, Paged, RoiData } from '../types'
import { api } from './client'

const base = (automationId: string) => `/automations/${automationId}`

export const getRoi = (automationId: string, signal?: AbortSignal) =>
  api<RoiData>(`${base(automationId)}/roi`, { signal })

export const listLogs = (automationId: string, page: number, pageSize = 10, signal?: AbortSignal) =>
  api<Paged<LogEntry>>(`${base(automationId)}/logs?page=${page}&pageSize=${pageSize}`, { signal })

/** Values use canonical units: percentages in percent points, durations in seconds. */
export const reportLog = (automationId: string, values: { metricId: string; value: number }[]) =>
  api<LogEntry>(`${base(automationId)}/logs`, { method: 'POST', json: { values } })
