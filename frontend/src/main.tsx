import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router'
import { router } from '@/app/router'
import { restoreSession } from '@/features/auth/auth-api'
import { authStore } from '@/features/auth/auth-store'
import { ApiError } from '@/lib/api-client'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      // Client errors (401/403/404/...) will not succeed on retry; network and 5xx errors might.
      retry: (failureCount, error) => !(error instanceof ApiError && error.status < 500) && failureCount < 2,
    },
  },
})

// Never let one person's cached data be shown to the next person signing in on the same tab
// (e.g. a shared reception computer): drop the whole cache whenever the signed-in user changes.
let cachedForUserId: string | null = null
authStore.subscribe(() => {
  const state = authStore.getState()
  const userId = state.status === 'authenticated' ? state.user.id : null
  if (userId !== cachedForUserId) {
    queryClient.clear()
    cachedForUserId = userId
  }
})

// Restore the session from the refresh cookie once, before the first route decision.
// Runs outside React so StrictMode's double effects cannot rotate the token twice.
void restoreSession()

const root = document.getElementById('root')
if (!root) throw new Error('Root element #root not found')

createRoot(root).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
)
