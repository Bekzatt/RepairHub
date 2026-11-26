import { Link } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { Wrench } from 'lucide-react'
import { useCurrentUser } from '@/features/auth/auth-store'
import { ordersKey } from '@/features/repair-orders/api'
import { StatusBadge } from '@/features/repair-orders/status-badge'
import { OPEN_STATUSES, type OrderListItem } from '@/features/repair-orders/types'
import { apiRequest } from '@/lib/api-client'
import { formatCalendarDate } from '@/lib/format'
import type { PagedResult } from '@/lib/paged'
import { cn } from '@/lib/utils'
import { t } from '@/locales'

/** The next open orders by expected date (technicians: their own). */
export function QueuePanel() {
  const user = useCurrentUser()
  const mine = user.role === 'Technician'
  const queue = useQuery({
    queryKey: [...ordersKey, 'list', 'queue', mine],
    queryFn: ({ signal }) => {
      const params = new URLSearchParams({ sort: 'ExpectedCompletionDate', desc: 'false', pageSize: '6' })
      for (const status of OPEN_STATUSES) params.append('status', status)
      if (mine) params.set('technician', 'me')
      return apiRequest<PagedResult<OrderListItem>>(`/api/repair-orders?${params}`, { signal })
    },
  })
  const items = queue.data?.items

  return (
    <section aria-labelledby="queue-title" className="flex flex-col rounded-xl border bg-card">
      <div className="flex items-baseline justify-between gap-3 px-5 pt-5 sm:px-6">
        <h2 id="queue-title" className="text-[15px] font-semibold">
          {t.dashboard.queue.title}
        </h2>
        <Link to="/orders" className="text-sm text-muted-foreground hover:text-foreground">
          {t.dashboard.queue.all}
        </Link>
      </div>
      <p className="px-5 text-sm text-muted-foreground sm:px-6">{t.dashboard.queue.description}</p>

      {items && items.length === 0 ? (
        <div className="flex flex-1 flex-col items-center justify-center px-6 py-12 text-center">
          <div className="mb-4 flex size-12 items-center justify-center rounded-full bg-accent text-accent-foreground">
            <Wrench className="size-5" strokeWidth={1.75} />
          </div>
          <p className="text-sm font-medium">{t.dashboard.queue.emptyTitle}</p>
          <p className="mt-1 max-w-sm text-sm text-muted-foreground">{t.dashboard.queue.emptyText}</p>
        </div>
      ) : (
        <ul className="mt-3 divide-y border-t">
          {(items ?? Array.from({ length: 3 }, () => null)).map((order, i) =>
            order ? (
              <li key={order.id}>
                <Link to={`/orders/${order.id}`} className="flex items-center gap-3 px-5 py-3 hover:bg-muted/50 sm:px-6">
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium">
                      <span className="text-muted-foreground tabular-nums">{order.number}</span> · {order.deviceBrand} {order.deviceModel}
                    </p>
                    <p className={cn('text-xs text-muted-foreground', order.isOverdue && 'font-medium text-destructive')}>
                      {order.expectedCompletionDate ? formatCalendarDate(order.expectedCompletionDate) : t.dashboard.queue.noDate}
                      {order.isOverdue && ` · ${t.order.overdue}`}
                    </p>
                  </div>
                  <StatusBadge status={order.status} />
                </Link>
              </li>
            ) : (
              <li key={i} className="px-5 py-3 sm:px-6">
                <span className="block h-8 animate-pulse rounded bg-muted" />
              </li>
            ),
          )}
        </ul>
      )}
    </section>
  )
}
