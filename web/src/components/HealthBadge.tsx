import { useEffect, useState } from 'react'
import { fetchHealth, type HealthResponse } from '../api/health'

export default function HealthBadge() {
  const [health, setHealth] = useState<HealthResponse | null>(null)
  const [down, setDown] = useState(false)

  useEffect(() => {
    const ctrl = new AbortController()
    fetchHealth(ctrl.signal)
      .then((h) => {
        setHealth(h)
        setDown(false)
      })
      .catch((e: Error) => e.name !== 'AbortError' && setDown(true))
    return () => ctrl.abort()
  }, [])

  const ok = !down && health?.status === 'Healthy'
  const label = down ? 'API unreachable' : health ? health.status : 'Checking...'
  return (
    <span className="flex items-center gap-1.5 text-xs text-gray-500" title={health?.checks.map((c) => `${c.name}: ${c.healthy ? 'ok' : 'down'}`).join(', ')}>
      <span className={`h-2 w-2 rounded-full ${ok ? 'bg-green-500' : health || down ? 'bg-red-500' : 'bg-gray-300'}`} />
      {label}
    </span>
  )
}
