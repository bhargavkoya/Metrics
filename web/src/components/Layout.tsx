import { Outlet } from 'react-router-dom'
import { useAuth } from '../auth/authContext'
import HealthBadge from './HealthBadge'

export default function Layout() {
  const { session, logout } = useAuth()
  const user = session!.user

  return (
    <div className="min-h-screen bg-gray-50">
      <header className="border-b border-gray-200 bg-white">
        <div className="mx-auto flex max-w-5xl items-center justify-between px-6 py-3">
          <span className="font-semibold">Automation Metrics</span>
          <div className="flex items-center gap-4 text-sm">
            <span>{user.name}</span>
            <span
              className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                user.team === 'Technical' ? 'bg-blue-100 text-blue-700' : 'bg-amber-100 text-amber-700'
              }`}
            >
              {user.team}
            </span>
            <button onClick={logout} className="text-gray-600 hover:text-gray-900">
              Log out
            </button>
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-5xl px-6 py-8">
        <Outlet />
      </main>
      <footer className="mx-auto max-w-5xl px-6 pb-6">
        <HealthBadge />
      </footer>
    </div>
  )
}
