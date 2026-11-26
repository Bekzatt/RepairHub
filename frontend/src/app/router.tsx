import { createBrowserRouter } from 'react-router'
import { AppShell } from '@/components/layout/app-shell'
import { placeholderModules, STAFF_MANAGER_ROLES } from '@/config/navigation'
import { RequireAuth, RequireRole } from '@/features/auth/route-guards'
import { DashboardPage } from '@/pages/dashboard-page'
import { LoginPage } from '@/pages/login-page'
import { ModulePlaceholderPage } from '@/pages/module-placeholder-page'
import { NotFoundPage, RouteErrorPage } from '@/pages/status-pages'
import { UsersPage } from '@/pages/users-page'

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage />, errorElement: <RouteErrorPage /> },
  {
    element: <RequireAuth />,
    errorElement: <RouteErrorPage />,
    children: [
      {
        path: '/',
        element: <AppShell />,
        children: [
          { index: true, element: <DashboardPage /> },
          {
            path: 'users',
            element: (
              <RequireRole roles={STAFF_MANAGER_ROLES}>
                <UsersPage />
              </RequireRole>
            ),
          },
          ...placeholderModules.map((module) => ({
            path: module.path.slice(1),
            element: <ModulePlaceholderPage module={module} />,
          })),
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
])
