export interface HealthCheck {
  name: string
  healthy: boolean
  error: string | null
}

export interface HealthResponse {
  status: 'Healthy' | 'Unhealthy'
  checks: HealthCheck[]
}

export async function fetchHealth(signal?: AbortSignal): Promise<HealthResponse> {
  const res = await fetch('/api/health', { signal })
  // 503 still carries a JSON body describing which dependency failed.
  if (res.status !== 200 && res.status !== 503) throw new Error(`HTTP ${res.status}`)
  return res.json()
}
