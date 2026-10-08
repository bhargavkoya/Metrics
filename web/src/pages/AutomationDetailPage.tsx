import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { getAutomation } from '../api/automations'
import { ApiError } from '../api/client'
import { formatDate } from '../lib/format'
import type { AutomationDetail } from '../types'

// Stub: documents (Phase 3) and the ROI tab (Phase 5) are added here later.
export default function AutomationDetailPage() {
  const { id } = useParams<{ id: string }>()
  const [automation, setAutomation] = useState<AutomationDetail | null>(null)
  const [error, setError] = useState<{ status: number; message: string } | null>(null)

  useEffect(() => {
    if (!id) return
    const ctrl = new AbortController()
    getAutomation(id, ctrl.signal)
      .then(setAutomation)
      .catch((e: Error) => {
        if (e.name === 'AbortError') return
        setError({ status: e instanceof ApiError ? e.status : 0, message: e.message })
      })
    return () => ctrl.abort()
  }, [id])

  const back = (
    <Link to="/" className="text-sm text-blue-600 hover:underline">
      &larr; Back to automations
    </Link>
  )

  if (error)
    return (
      <div>
        {back}
        <p className="mt-4 text-gray-700">
          {error.status === 404 ? 'This automation does not exist.' : `Could not load automation: ${error.message}`}
        </p>
      </div>
    )

  if (!automation)
    return (
      <div>
        {back}
        <p className="mt-4 text-gray-500">Loading...</p>
      </div>
    )

  return (
    <div>
      {back}
      <div className="mt-3 rounded-lg border border-gray-200 bg-white p-6">
        <span className="rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-600">{automation.department}</span>
        <h1 className="mt-2 text-xl font-semibold">{automation.name}</h1>
        <p className="mt-1 text-gray-600">{automation.description}</p>

        <dl className="mt-6 grid gap-4 sm:grid-cols-2">
          <div>
            <dt className="text-xs font-medium uppercase text-gray-500">Client</dt>
            <dd className="mt-1">{automation.client}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium uppercase text-gray-500">Last activity</dt>
            <dd className="mt-1">{formatDate(automation.lastActivityAt)}</dd>
          </div>
          <div className="sm:col-span-2">
            <dt className="text-xs font-medium uppercase text-gray-500">Business requirement</dt>
            <dd className="mt-1 whitespace-pre-line">{automation.requirement}</dd>
          </div>
        </dl>
      </div>
      <p className="mt-4 text-xs text-gray-400">Related documents and the ROI tab arrive in later phases.</p>
    </div>
  )
}
