export type Team = 'Business' | 'Technical'

export interface User {
  id: string
  email: string
  name: string
  team: Team
}

export interface AuthResponse {
  token: string
  expiresAt: string
  user: User
}

export interface Session {
  token: string
  expiresAt: string
  user: User
}

export interface AutomationCard {
  id: string
  name: string
  description: string
  department: string
  lastActivityAt: string
}

export interface AutomationDetail extends AutomationCard {
  client: string
  requirement: string
  createdAt: string
}

export interface DocumentItem {
  id: string
  originalFileName: string
  contentType: string
  sizeBytes: number
  uploadedBy: string
  uploadedAt: string
}

export type MetricKind = 'Input' | 'Computed'
export type MetricValueType = 'Number' | 'Percentage' | 'Currency' | 'Duration'

export interface MetricDefinition {
  id: string
  label: string
  kind: MetricKind
  valueType: MetricValueType
  currencyCode: string | null
  formulaText: string | null
  createdAt: string
}

export interface FormulaError {
  code: string
  message: string
  position: number
}

export interface ValidateFormulaResult {
  valid: boolean
  resultType: MetricValueType | null
  currencyCode: string | null
  references: string[]
  errors: FormulaError[]
}

export type LogValueRole = 'Input' | 'Computed'

export interface LogValue {
  metricDefinitionId: string
  label: string
  valueType: MetricValueType
  currencyCode: string | null
  role: LogValueRole
  value: number | null
  formula: string | null
}

export interface LogEntry {
  id: string
  reportedAt: string
  reportedBy: string
  values: LogValue[]
}

export interface Paged<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface CurrentFigure {
  metricDefinitionId: string
  label: string
  kind: MetricKind
  valueType: MetricValueType
  currencyCode: string | null
  formula: string | null
  hasValue: boolean
  value: number | null
}

export interface SeriesPoint {
  logId: string
  reportedAt: string
  value: number | null
}

export interface Series {
  metricDefinitionId: string
  label: string
  kind: MetricKind
  valueType: MetricValueType
  currencyCode: string | null
  points: SeriesPoint[]
}

export interface RoiData {
  automationId: string
  dataVersion: number
  asOf: string | null
  reportedBy: string | null
  current: CurrentFigure[]
  series: Series[]
}
