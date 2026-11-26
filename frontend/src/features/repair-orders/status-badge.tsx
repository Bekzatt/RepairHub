import { cn } from '@/lib/utils'
import { t } from '@/locales'
import type { OrderStatus, RepairOutcome } from './types'

/**
 * Colour carries meaning: teal = the shop is working on it, amber = waiting on the customer or a supplier,
 * neutral = not started / finished, red = cancelled.
 */
const tone: Record<OrderStatus, string> = {
  Received: 'border-border bg-card text-foreground',
  Diagnostics: 'border-transparent bg-accent text-accent-foreground',
  AwaitingApproval: 'border-transparent bg-warning-soft text-warning',
  WaitingForParts: 'border-warning/30 bg-card text-warning',
  InRepair: 'border-transparent bg-primary text-primary-foreground',
  ReadyForPickup: 'border-transparent bg-warning text-white',
  Issued: 'border-transparent bg-secondary text-muted-foreground',
  Cancelled: 'border-transparent bg-destructive/10 text-destructive',
}

export function StatusBadge({ status, className }: { status: OrderStatus; className?: string }) {
  return (
    <span
      className={cn(
        'inline-flex h-6 shrink-0 items-center rounded-full border px-2.5 text-xs font-medium whitespace-nowrap',
        tone[status],
        className,
      )}
    >
      {t.orderStatus[status]}
    </span>
  )
}

export function OutcomeLabel({ outcome }: { outcome: RepairOutcome }) {
  return (
    <span className={cn('text-xs font-medium', outcome === 'Repaired' ? 'text-primary' : 'text-warning')}>
      {t.outcome[outcome]}
    </span>
  )
}
