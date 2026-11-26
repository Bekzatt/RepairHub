import { useSyncExternalStore } from 'react'
import type { AuthResponse, CurrentUser } from './types'

/**
 * Session state. The access token lives only in memory (never in localStorage),
 * so injected scripts cannot read a long-lived credential. After a page reload the session
 * is restored from the HttpOnly refresh cookie via /api/auth/refresh.
 */
export type AuthState =
  | { status: 'loading' }
  | { status: 'anonymous' }
  | { status: 'authenticated'; user: CurrentUser }

let state: AuthState = { status: 'loading' }
let accessToken: string | null = null
const listeners = new Set<() => void>()

function emit(next: AuthState) {
  state = next
  listeners.forEach((listener) => listener())
}

export const authStore = {
  getState: () => state,
  getAccessToken: () => accessToken,

  setSession(response: AuthResponse) {
    accessToken = response.accessToken
    emit({ status: 'authenticated', user: response.user })
  },

  updateUser(user: CurrentUser) {
    if (state.status === 'authenticated') emit({ status: 'authenticated', user })
  },

  clear() {
    accessToken = null
    emit({ status: 'anonymous' })
  },

  subscribe(listener: () => void) {
    listeners.add(listener)
    return () => listeners.delete(listener)
  },
}

export function useAuth(): AuthState {
  return useSyncExternalStore(authStore.subscribe, authStore.getState)
}

/** For components rendered only inside protected routes. */
export function useCurrentUser(): CurrentUser {
  const auth = useAuth()
  if (auth.status !== 'authenticated') throw new Error('useCurrentUser used outside an authenticated route')
  return auth.user
}
