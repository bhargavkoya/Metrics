import { getToken } from '../auth/session'

export class ApiError extends Error {
  status: number
  fieldErrors: Record<string, string[]>

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

// Set by AuthProvider so a 401 anywhere logs the user out.
let onUnauthorized: (() => void) | null = null
export function setUnauthorizedHandler(fn: (() => void) | null) {
  onUnauthorized = fn
}

export async function api<T>(path: string, init: RequestInit & { json?: unknown } = {}): Promise<T> {
  const headers = new Headers(init.headers)
  const token = getToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (init.json !== undefined) headers.set('Content-Type', 'application/json')

  const res = await fetch(`/api${path}`, {
    ...init,
    headers,
    body: init.json !== undefined ? JSON.stringify(init.json) : init.body,
  })

  if (res.ok) return (res.status === 204 ? undefined : await res.json()) as T

  // ProblemDetails: { title, detail, errors? }
  let body: { title?: string; detail?: string; errors?: Record<string, string[]> } = {}
  try {
    body = await res.json()
  } catch {
    /* non-JSON error body */
  }

  // A 401 on login itself means bad credentials, not an expired session.
  if (res.status === 401 && token) onUnauthorized?.()

  throw new ApiError(res.status, body.detail ?? body.title ?? `HTTP ${res.status}`, body.errors)
}
