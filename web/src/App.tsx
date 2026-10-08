import { useEffect, useState } from 'react'
import { fetchHealth, type HealthResponse } from './api/health'

export default function App() {
  const [health, setHealth] = useState<HealthResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const ctrl = new AbortController()
    fetchHealth(ctrl.signal)
      .then(setHealth)
      .catch((e: Error) => e.name !== 'AbortError' && setError(e.message))
    return () => ctrl.abort()
  }, [])

  return (
    <main className="mx-auto max-w-xl p-8">
      <h1 className="text-2xl font-semibold">Automation Metrics Dashboard</h1>
      <p className="mt-1 text-sm text-gray-500">Phase 0 scaffold: API health check</p>

      <section className="mt-6 rounded-lg border border-gray-200 p-4">
        {error && <p className="text-red-600">API unreachable: {error}</p>}
        {!error && !health && <p className="text-gray-500">Checking...</p>}
        {health && (
          <>
            <p className={health.status === 'Healthy' ? 'text-green-600' : 'text-red-600'}>
              Status: {health.status}
            </p>
            <ul className="mt-2 space-y-1 text-sm">
              {health.checks.map((c) => (
                <li key={c.name}>
                  {c.healthy ? '✅' : '❌'} {c.name}
                  {c.error && <span className="text-red-600"> ({c.error})</span>}
                </li>
              ))}
            </ul>
          </>
        )}
      </section>
    </main>
  )
}
