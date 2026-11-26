import { apiRequest, refreshSession } from '@/lib/api-client'
import { authStore } from './auth-store'
import type { AuthResponse } from './types'

export async function login(email: string, password: string): Promise<void> {
  const response = await apiRequest<AuthResponse>('/api/auth/login', {
    method: 'POST',
    body: { email, password },
    auth: false,
  })
  authStore.setSession(response)
}

export async function logout(): Promise<void> {
  try {
    await apiRequest<void>('/api/auth/logout', { method: 'POST', auth: false })
  } finally {
    authStore.clear()
  }
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  const response = await apiRequest<AuthResponse>('/api/auth/change-password', {
    method: 'POST',
    body: { currentPassword, newPassword },
  })
  authStore.setSession(response)
}

/**
 * Restores the session on page load from the refresh cookie.
 * Retries once: if two tabs refresh at the same moment, one of them briefly loses the race.
 */
export async function restoreSession(): Promise<void> {
  if (await refreshSession()) return
  await new Promise((resolve) => setTimeout(resolve, 400))
  if (await refreshSession()) return
  authStore.clear()
}
