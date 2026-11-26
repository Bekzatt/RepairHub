import { useQuery } from '@tanstack/react-query'

export type SystemStatus = 'checking' | 'online' | 'degraded' | 'offline'

const REFRESH_INTERVAL_MS = 30_000

/** Health endpoints answer with a status code; any non-2xx (or network failure) means "not healthy". */
async function probe(path: string, signal: AbortSignal): Promise<boolean> {
  try {
    const response = await fetch(path, { signal, headers: { Accept: 'text/plain' } })
    return response.ok
  } catch (error) {
    if (signal.aborted) throw error
    return false
  }
}

function useProbe(path: string) {
  return useQuery({
    queryKey: ['health', path],
    queryFn: ({ signal }) => probe(path, signal),
    refetchInterval: REFRESH_INTERVAL_MS,
    retry: false,
  })
}

/**
 * Combines liveness (API process up) and readiness (database reachable)
 * into one status the UI can show.
 */
export function useSystemHealth() {
  const live = useProbe('/health/live')
  const ready = useProbe('/health/ready')

  let status: SystemStatus
  if (live.data === undefined || ready.data === undefined) status = 'checking'
  else if (!live.data) status = 'offline'
  else if (!ready.data) status = 'degraded'
  else status = 'online'

  return {
    status,
    apiUp: live.data,
    databaseUp: live.data === false ? undefined : ready.data,
    isFetching: live.isFetching || ready.isFetching,
    checkedAt: Math.max(live.dataUpdatedAt, ready.dataUpdatedAt) || undefined,
    refresh: () => Promise.all([live.refetch(), ready.refetch()]),
  }
}
