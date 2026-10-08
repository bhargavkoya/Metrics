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
