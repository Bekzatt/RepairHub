import { cn } from '@/lib/utils'
import { ru } from '@/locales/ru'
import type { SystemStatus } from '@/features/system/use-system-health'

const dotClass: Record<SystemStatus, string> = {
  checking: 'bg-muted-foreground/50 animate-pulse',
  online: 'bg-success',
  degraded: 'bg-warning',
  offline: 'bg-destructive',
}

export function SystemStatusBadge({ status }: { status: SystemStatus }) {
  return (
    <span
      role="status"
      className="inline-flex h-8 items-center gap-2 rounded-full border bg-card px-3 text-xs font-medium text-foreground"
    >
      <span aria-hidden className={cn('size-2 rounded-full', dotClass[status])} />
      {ru.status.summary[status]}
    </span>
  )
}
