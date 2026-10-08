import { createContext, useContext } from 'react'
import type { AuthResponse, Session, Team } from '../types'

export interface AuthContextValue {
  session: Session | null
  isTechnical: boolean
  login: (email: string, password: string) => Promise<void>
  register: (email: string, password: string, name: string, team: Team) => Promise<void>
  logout: () => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}

export const toSession = (r: AuthResponse): Session => ({ token: r.token, expiresAt: r.expiresAt, user: r.user })
