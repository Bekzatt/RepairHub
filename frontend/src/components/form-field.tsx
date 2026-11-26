import { useId, type ReactNode } from 'react'
import { Label } from '@/components/ui/label'

interface FormFieldProps {
  label: string
  error?: string
  hint?: ReactNode
  optionalLabel?: string
  children: (props: { id: string; 'aria-invalid': boolean; 'aria-describedby'?: string }) => ReactNode
}

/** Label + control + hint/error, with the accessibility wiring done once. */
export function FormField({ label, error, hint, optionalLabel, children }: FormFieldProps) {
  const id = useId()
  const describedBy = error ? `${id}-error` : hint ? `${id}-hint` : undefined

  return (
    <div className="space-y-1.5">
      <Label htmlFor={id} className="flex items-baseline gap-2">
        {label}
        {optionalLabel && <span className="text-xs font-normal text-muted-foreground">{optionalLabel}</span>}
      </Label>
      {children({ id, 'aria-invalid': Boolean(error), 'aria-describedby': describedBy })}
      {error ? (
        <p id={`${id}-error`} className="text-xs text-destructive">
          {error}
        </p>
      ) : hint ? (
        <p id={`${id}-hint`} className="text-xs text-muted-foreground">
          {hint}
        </p>
      ) : null}
    </div>
  )
}

export function FormAlert({ children }: { children: ReactNode }) {
  return (
    <div role="alert" className="rounded-md border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive">
      {children}
    </div>
  )
}
