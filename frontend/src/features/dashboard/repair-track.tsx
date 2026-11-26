import { Link } from 'react-router'
import { useCurrentUser } from '@/features/auth/auth-store'
import { useOrderSummary } from '@/features/repair-orders/api'
import { OPEN_STATUSES, type OrderStatus } from '@/features/repair-orders/types'
import { cn } from '@/lib/utils'
import { t } from '@/locales'

/** Stages where the order waits on the customer rather than on the shop. */
const WAITS_FOR_CUSTOMER: readonly OrderStatus[] = ['AwaitingApproval', 'ReadyForPickup']

/** Open orders per status. Each stage links to the filtered order list. */
export function RepairTrack() {
  const user = useCurrentUser()
  const scope = user.role === 'Technician' ? 'mine' : 'all'
  const summary = useOrderSummary(scope)
  const counts = new Map(summary.data?.byStatus.map((s) => [s.status, s.count]))

  return (
    <section aria-labelledby="track-title" className="rounded-xl border bg-card">
      <header className="flex flex-col gap-1 px-5 pt-5 sm:px-6">
        <h2 id="track-title" className="text-[15px] font-semibold">
          {t.dashboard.track.title}
        </h2>
        <p className="max-w-prose text-sm text-muted-foreground">{t.dashboard.track.description}</p>
      </header>

      <ol className="grid gap-px px-5 pt-5 pb-5 sm:px-6 lg:grid-cols-6 lg:gap-0">
        {OPEN_STATUSES.map((status, index) => {
          const waits = WAITS_FOR_CUSTOMER.includes(status)
          const count = counts.get(status)
          return (
            <li key={status} className="relative pb-5 last:pb-0 lg:pr-4 lg:pb-0">
              <Link
                to={`/orders?status=${status}${scope === 'mine' ? '' : '&scope=all'}`}
                className="group flex gap-4 rounded-md focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none lg:flex-col lg:gap-3"
              >
                <div className="relative flex flex-col items-center lg:flex-row">
                  <span
                    className={cn(
                      'z-10 flex size-7 shrink-0 items-center justify-center rounded-full border-2 bg-card text-xs font-semibold tabular-nums',
                      waits ? 'border-warning text-warning' : 'border-primary text-primary',
                    )}
                  >
                    {index + 1}
                  </span>
                  {index < OPEN_STATUSES.length - 1 && (
                    <span
                      aria-hidden
                      className="absolute top-7 bottom-[-20px] left-1/2 w-px -translate-x-1/2 bg-border lg:top-1/2 lg:right-[-16px] lg:bottom-auto lg:left-7 lg:h-px lg:w-auto lg:translate-x-0"
                    />
                  )}
                </div>

                <div className="min-w-0 flex-1">
                  <div className="flex items-baseline justify-between gap-3 lg:block">
                    <h3 className="text-sm font-semibold group-hover:underline">{t.orderStatus[status]}</h3>
                    <p
                      className={cn(
                        'text-lg font-semibold tabular-nums lg:mt-2 lg:text-2xl',
                        count ? 'text-foreground' : 'text-muted-foreground/70',
                      )}
                    >
                      {count ?? (summary.isLoading ? '…' : 0)}
                      <span className="sr-only"> {t.dashboard.track.orders}</span>
                    </p>
                  </div>
                  <p className="mt-0.5 text-xs leading-relaxed text-muted-foreground lg:mt-1">{t.orderStatusHint[status]}</p>
                  {waits && (
                    <span className="mt-2 inline-block rounded-full bg-warning-soft px-2 py-0.5 text-[11px] font-medium text-warning">
                      {t.dashboard.track.waiting}
                    </span>
                  )}
                </div>
              </Link>
            </li>
          )
        })}
      </ol>
    </section>
  )
}
