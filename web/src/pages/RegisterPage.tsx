import { useState, type FormEvent } from 'react'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { ApiError } from '../api/client'
import { useAuth } from '../auth/authContext'
import { AuthCard, Field, inputClass } from '../components/AuthForm'
import type { Team } from '../types'

export default function RegisterPage() {
  const { session, register } = useAuth()
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [team, setTeam] = useState<Team>('Business')
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [busy, setBusy] = useState(false)

  if (session) return <Navigate to="/" replace />

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    setFieldErrors({})
    try {
      await register(email, password, name, team)
      navigate('/', { replace: true })
    } catch (err) {
      if (err instanceof ApiError) {
        setFieldErrors(err.fieldErrors)
        if (Object.keys(err.fieldErrors).length === 0) setError(err.message)
      } else setError('Something went wrong')
    } finally {
      setBusy(false)
    }
  }

  const fe = (k: string) => fieldErrors[k]?.[0]

  return (
    <AuthCard title="Create account">
      <form onSubmit={onSubmit} className="space-y-4">
        <Field label="Name" error={fe('name')}>
          <input className={inputClass} value={name} onChange={(e) => setName(e.target.value)} required autoFocus />
        </Field>
        <Field label="Email" error={fe('email')}>
          <input className={inputClass} type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </Field>
        <Field label="Password (min 8 characters)" error={fe('password')}>
          <input className={inputClass} type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </Field>
        <Field label="Team" error={fe('team')}>
          <select className={inputClass} value={team} onChange={(e) => setTeam(e.target.value as Team)}>
            <option value="Business">Business (read-only)</option>
            <option value="Technical">Technical (define and report metrics)</option>
          </select>
        </Field>
        {error && <p className="text-sm text-red-600">{error}</p>}
        <button disabled={busy} className="w-full rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
          {busy ? 'Creating...' : 'Create account'}
        </button>
      </form>
      <p className="mt-4 text-sm text-gray-600">
        Already registered? <Link to="/login" className="text-blue-600 hover:underline">Sign in</Link>
      </p>
    </AuthCard>
  )
}
