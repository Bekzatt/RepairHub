import type { ReactNode } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router'
import { ShieldAlert } from 'lucide-react'
import { ru } from '@/locales/ru'
import { useAuth } from './auth-store'
import type { UserRole } from './types'

/** Renders child routes only for signed-in users; otherwise sends them to /login and back afterwards. */
export function RequireAuth() {
  const auth = useAuth()
  const location = useLocation()

  if (auth.status === 'loading') return <FullScreenLoader />
  if (auth.status === 'anonymous') {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  }
  return <Outlet />
}

/**
 * Hides a page from roles that cannot use it. This is a convenience only —
 * the API enforces the same rules and returns 403 regardless of the UI.
 */
export function RequireRole({ roles, children }: { roles: readonly UserRole[]; children: ReactNode }) {
  const auth = useAuth()
  if (auth.status !== 'authenticated' || !roles.includes(auth.user.role)) {
    return (
      <div className="mx-auto max-w-md py-16 text-center">
        <ShieldAlert className="mx-auto mb-4 size-8 text-muted-foreground" strokeWidth={1.75} />
        <h1 className="text-xl font-semibold">{ru.forbidden.title}</h1>
        <p className="mt-2 text-sm text-muted-foreground">{ru.forbidden.text}</p>
      </div>
    )
  }
  return children
}

export function FullScreenLoader() {
  return (
    <div className="flex min-h-dvh items-center justify-center" role="status" aria-live="polite">
      <span className="size-6 animate-spin rounded-full border-2 border-primary border-t-transparent" />
      <span className="sr-only">{ru.status.loading}</span>
    </div>
  )
}
