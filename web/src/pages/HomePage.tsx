import { useAuth } from '../auth/authContext'

// Placeholder until the catalog lands in Phase 2.
export default function HomePage() {
  const { session, isTechnical } = useAuth()
  return (
    <section className="rounded-lg border border-gray-200 bg-white p-6">
      <h1 className="text-xl font-semibold">Welcome, {session!.user.name}</h1>
      <p className="mt-2 text-sm text-gray-600">
        {isTechnical
          ? 'Technical team: you will be able to define metrics, report values and manage documents.'
          : 'Business team: you have read-only access to automations and their ROI data.'}
      </p>
      <p className="mt-4 text-xs text-gray-400">The automation catalog arrives in Phase 2.</p>
    </section>
  )
}
