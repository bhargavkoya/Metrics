import { Link } from 'react-router-dom'
import { formatRelativeDays } from '../lib/format'
import type { AutomationCard as Card } from '../types'

export default function AutomationCard({ automation }: { automation: Card }) {
  return (
    <article className="flex flex-col rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
      <span className="w-fit rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-600">{automation.department}</span>
      <h2 className="mt-2 font-semibold">{automation.name}</h2>
      <p className="mt-1 flex-1 text-sm text-gray-600">{automation.description}</p>
      <div className="mt-4 flex items-center justify-between">
        <span className="text-xs text-gray-400">Active {formatRelativeDays(automation.lastActivityAt)}</span>
        <Link
          to={`/automations/${automation.id}`}
          className="rounded-md bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700"
        >
          Details
        </Link>
      </div>
    </article>
  )
}
