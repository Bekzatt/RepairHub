import { createBrowserRouter } from 'react-router'
import { AppShell } from '@/components/layout/app-shell'
import { placeholderModules } from '@/config/navigation'
import { DashboardPage } from '@/pages/dashboard-page'
import { ModulePlaceholderPage } from '@/pages/module-placeholder-page'
import { NotFoundPage, RouteErrorPage } from '@/pages/status-pages'

export const router = createBrowserRouter([
  {
    path: '/',
    element: <AppShell />,
    errorElement: <RouteErrorPage />,
    children: [
      { index: true, element: <DashboardPage /> },
      ...placeholderModules.map((module) => ({
        path: module.path.slice(1),
        element: <ModulePlaceholderPage module={module} />,
      })),
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])
