import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import * as authApi from '../api/auth'
import { setUnauthorizedHandler } from '../api/client'
import type { Session } from '../types'
import { AuthContext, toSession, type AuthContextValue } from './authContext'
import { clearSession, loadSession, saveSession } from './session'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(() => loadSession())

  const logout = useCallback(() => {
    clearSession()
    setSession(null)
  }, [])

  useEffect(() => {
    setUnauthorizedHandler(logout)
    return () => setUnauthorizedHandler(null)
  }, [logout])

  const value = useMemo<AuthContextValue>(() => {
    const start = (s: Session) => {
      saveSession(s)
      setSession(s)
    }
    return {
      session,
      isTechnical: session?.user.team === 'Technical',
      login: async (email, password) => start(toSession(await authApi.login(email, password))),
      register: async (email, password, name, team) =>
        start(toSession(await authApi.register(email, password, name, team))),
      logout,
    }
  }, [session, logout])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
