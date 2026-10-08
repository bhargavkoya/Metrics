import type { Session } from '../types'

const KEY = 'metrics.session'

export function loadSession(): Session | null {
  try {
    const raw = localStorage.getItem(KEY)
    if (!raw) return null
    const s = JSON.parse(raw) as Session
    if (new Date(s.expiresAt).getTime() <= Date.now()) {
      localStorage.removeItem(KEY)
      return null
    }
    return s
  } catch {
    return null
  }
}

export function saveSession(s: Session) {
  localStorage.setItem(KEY, JSON.stringify(s))
}

export function clearSession() {
  localStorage.removeItem(KEY)
}

export function getToken(): string | null {
  return loadSession()?.token ?? null
}
