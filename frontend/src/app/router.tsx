import type { ReactNode } from 'react'
import { createBrowserRouter } from 'react-router'
import { AppShell } from '@/components/layout/app-shell'
import { FRONT_OFFICE_ROLES, placeholderModules, STAFF_MANAGER_ROLES } from '@/config/navigation'
import { RequireAuth, RequireRole } from '@/features/auth/route-guards'
import { CustomerDetailsPage } from '@/pages/customer-details-page'
import { CustomersPage } from '@/pages/customers-page'
import { DashboardPage } from '@/pages/dashboard-page'
import { DeviceDetailsPage } from '@/pages/device-details-page'
import { DevicesPage } from '@/pages/devices-page'
import { LoginPage } from '@/pages/login-page'
import { ModulePlaceholderPage } from '@/pages/module-placeholder-page'
import { OrderCreatePage } from '@/pages/order-create-page'
import { OrderDetailsPage } from '@/pages/order-details-page'
import { OrdersPage } from '@/pages/orders-page'
import { NotFoundPage, RouteErrorPage } from '@/pages/status-pages'
import { UsersPage } from '@/pages/users-page'

/** Customers, devices and intake: Owner, Admin, Receptionist (the API enforces the same rule). */
const frontOffice = (page: ReactNode) => <RequireRole roles={FRONT_OFFICE_ROLES}>{page}</RequireRole>

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
          { path: 'orders', element: <OrdersPage /> },
          { path: 'orders/new', element: frontOffice(<OrderCreatePage />) },
          { path: 'orders/:id', element: <OrderDetailsPage /> },
          { path: 'customers', element: frontOffice(<CustomersPage />) },
          { path: 'customers/:id', element: frontOffice(<CustomerDetailsPage />) },
          { path: 'devices', element: frontOffice(<DevicesPage />) },
          { path: 'devices/:id', element: frontOffice(<DeviceDetailsPage />) },
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
