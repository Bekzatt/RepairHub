import { authStore } from '@/features/auth/auth-store'
import type { AuthResponse } from '@/features/auth/types'

/** Sent on cookie-authenticated auth endpoints; the API rejects refresh/logout without it (CSRF defence). */
const CLIENT_HEADER = { 'X-Requested-With': 'RepairHub' } as const

export class ApiError extends Error {
  readonly status: number
  readonly code: string | null
  readonly fieldErrors: Record<string, string[]>
  /** The whole problem-details body, for extra data such as "matches" of customer_phone_exists. */
  readonly body: Record<string, unknown>

  constructor(
    status: number,
    code: string | null,
    fieldErrors: Record<string, string[]> = {},
    body: Record<string, unknown> = {},
  ) {
    super(`API request failed with status ${status}${code ? ` (${code})` : ''}`)
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
    this.body = body
  }
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  body?: unknown
  /** Attach the access token and retry once after refreshing on 401. Default: true. */
  auth?: boolean
  signal?: AbortSignal
}

let refreshInFlight: Promise<boolean> | null = null

/**
 * Exchanges the refresh cookie for a new access token. Concurrent callers share one request,
 * because the server rotates the token on every use.
 */
export function refreshSession(): Promise<boolean> {
  refreshInFlight ??= (async () => {
    try {
      const response = await fetch('/api/auth/refresh', { method: 'POST', headers: CLIENT_HEADER })
      if (!response.ok) return false
      authStore.setSession((await response.json()) as AuthResponse)
      return true
    } catch {
      return false
    } finally {
      refreshInFlight = null
    }
  })()
  return refreshInFlight
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { auth = true } = options

  let response = await send(path, options, auth)
  if (response.status === 401 && auth) {
    if (await refreshSession()) {
      response = await send(path, options, auth)
    }
    if (response.status === 401) {
      authStore.clear()
    }
  }

  if (!response.ok) throw await toApiError(response)
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

function send(path: string, { method = 'GET', body, signal }: RequestOptions, auth: boolean) {
  const headers: Record<string, string> = { Accept: 'application/json', ...CLIENT_HEADER }
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  const token = authStore.getAccessToken()
  if (auth && token) headers.Authorization = `Bearer ${token}`

  return fetch(path, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  })
}

async function toApiError(response: Response): Promise<ApiError> {
  try {
    const problem = (await response.json()) as { code?: string; errors?: Record<string, string[]> } & Record<string, unknown>
    return new ApiError(response.status, problem.code ?? null, problem.errors ?? {}, problem)
  } catch {
    return new ApiError(response.status, null)
  }
}
