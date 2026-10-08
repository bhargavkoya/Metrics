import type { AuthResponse, Team, User } from '../types'
import { api } from './client'

export const login = (email: string, password: string) =>
  api<AuthResponse>('/auth/login', { method: 'POST', json: { email, password } })

export const register = (email: string, password: string, name: string, team: Team) =>
  api<AuthResponse>('/auth/register', { method: 'POST', json: { email, password, name, team } })

export const me = () => api<User>('/auth/me')
