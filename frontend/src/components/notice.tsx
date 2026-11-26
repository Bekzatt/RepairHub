import { useCallback, useEffect, useState } from 'react'
import { errorMessage } from '@/lib/form-errors'

/** Success/failure messages shown at the top of a page after an action. */
export function useNotice() {
  const [flash, setFlash] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!flash) return
    const timer = setTimeout(() => setFlash(null), 4000)
    return () => clearTimeout(timer)
  }, [flash])

  const done = useCallback((message: string) => {
    setError(null)
    setFlash(message)
  }, [])

  const fail = useCallback((cause: unknown) => {
    setFlash(null)
    setError(typeof cause === 'string' ? cause : errorMessage(cause))
  }, [])

  const clear = useCallback(() => {
    setFlash(null)
    setError(null)
  }, [])

  return { flash, error, done, fail, clear }
}

export function Notice({ flash, error }: { flash: string | null; error: string | null }) {
  return (
    <div aria-live="polite">
      {flash && (
        <p role="status" className="rounded-md bg-accent px-3 py-2 text-sm text-accent-foreground">
          {flash}
        </p>
      )}
      {error && (
        <p role="alert" className="rounded-md border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive">
          {error}
        </p>
      )}
    </div>
  )
}
