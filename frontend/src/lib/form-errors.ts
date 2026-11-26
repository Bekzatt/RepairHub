import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiError } from '@/lib/api-client'
import { t } from '@/locales'

/** A user-facing Russian message for any error thrown by the API client. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code && error.code in t.errors) return t.errors[error.code as keyof typeof t.errors]
    if (error.status === 403) return t.errors.forbidden
    if (error.status === 404) return t.errors.not_found
    return t.errors.unknown
  }
  if (error instanceof TypeError) return t.errors.network // fetch network failure
  return t.errors.unknown
}

/**
 * Copies server-side field errors (400 validation) onto form fields.
 * Returns true when at least one field error was applied.
 */
export function applyFieldErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  fields: readonly Path<T>[],
  messages: Partial<Record<Path<T>, string>> = {},
): boolean {
  if (!(error instanceof ApiError) || error.status !== 400) return false
  let applied = false
  for (const field of fields) {
    const serverMessages = error.fieldErrors[field]
    if (serverMessages?.length) {
      setError(field, { message: messages[field] ?? serverMessages[0] })
      applied = true
    }
  }
  return applied
}
