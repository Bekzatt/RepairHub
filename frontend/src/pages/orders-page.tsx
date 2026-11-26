import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { AlarmClock, Plus, Search } from 'lucide-react'
import { Pager } from '@/components/pager'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { FRONT_OFFICE_ROLES } from '@/config/navigation'
import { useCurrentUser } from '@/features/auth/auth-store'
import { useOrders, useOrderSummary } from '@/features/repair-orders/api'
import { OrdersTable } from '@/features/repair-orders/orders-table'
import { OPEN_STATUSES, ORDER_STATUSES, type OrderStatus } from '@/features/repair-orders/types'
import { cn } from '@/lib/utils'
import { t } from '@/locales'

type Scope = 'mine' | 'unassigned' | 'all'
const SCOPES: Scope[] = ['mine', 'unassigned', 'all']

/** "open" (default), "all" or a single status. */
type StatusFilter = 'open' | 'all' | OrderStatus

function parseStatus(value: string | null): StatusFilter {
  if (value === 'all') return 'all'
  return ORDER_STATUSES.includes(value as OrderStatus) ? (value as OrderStatus) : 'open'
}

/**
 * The repair order list. Filters live in the URL, so links from the dashboard work, the back button
 * restores the list and a filtered list can be shared with a colleague.
 */
export function OrdersPage() {
  const user = useCurrentUser()
  const [params, setParams] = useSearchParams()
  const defaultScope: Scope = user.role === 'Technician' ? 'mine' : 'all'
  const scope = SCOPES.includes(params.get('scope') as Scope) ? (params.get('scope') as Scope) : defaultScope
  const status = parseStatus(params.get('status'))
  const overdue = params.get('overdue') === '1'
  const page = Math.max(1, Number(params.get('page')) || 1)
  const search = params.get('q') ?? ''
  const [searchInput, setSearchInput] = useState(search)

  function update(changes: Record<string, string | null>) {
    setParams(
      (current) => {
        const next = new URLSearchParams(current)
        for (const [key, value] of Object.entries(changes)) {
          if (value === null || value === '') next.delete(key)
          else next.set(key, value)
        }
        if (!('page' in changes)) next.delete('page')
        return next
      },
      { replace: true },
    )
  }

  // Debounced search box → URL.
  useEffect(() => {
    if (searchInput === search) return
    const timer = setTimeout(() => update({ q: searchInput.trim() }), 300)
    return () => clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchInput])

  const statuses: OrderStatus[] = status === 'open' ? [...OPEN_STATUSES] : status === 'all' ? [] : [status]
  const orders = useOrders({ search, statuses, technician: scope, overdue, page })
  const summary = useOrderSummary(scope)
  const counts = new Map(summary.data?.byStatus.map((s) => [s.status, s.count]))
  const openCount = OPEN_STATUSES.reduce((sum, s) => sum + (counts.get(s) ?? 0), 0)
  const canCreate = FRONT_OFFICE_ROLES.includes(user.role)

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight sm:text-[28px]">{t.orders.title}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t.orders.description}</p>
        </div>
        {canCreate && (
          <Button asChild className="self-start sm:self-auto">
            <Link to="/orders/new">
              <Plus />
              {t.orders.new}
            </Link>
          </Button>
        )}
      </div>

      {/* Whose orders */}
      <div role="tablist" aria-label={t.orders.scopeLabel} className="inline-flex rounded-lg border bg-card p-1">
        {SCOPES.map((s) => (
          <button
            key={s}
            role="tab"
            aria-selected={scope === s}
            onClick={() => update({ scope: s === defaultScope ? null : s })}
            className={cn(
              'inline-flex h-8 items-center gap-2 rounded-md px-3 text-sm font-medium transition-colors',
              scope === s ? 'bg-sidebar text-white' : 'text-muted-foreground hover:text-foreground',
            )}
          >
            {t.orders.scope[s]}
            {s === 'unassigned' && summary.data && summary.data.unassigned > 0 && (
              <span
                className={cn(
                  'rounded-full px-1.5 text-xs tabular-nums',
                  scope === s ? 'bg-white/20' : 'bg-warning-soft text-warning',
                )}
              >
                {summary.data.unassigned}
              </span>
            )}
          </button>
        ))}
      </div>

      <section className="rounded-xl border bg-card">
        <div className="space-y-3 border-b p-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
            <div className="relative flex-1">
              <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                placeholder={t.orders.searchPlaceholder}
                aria-label={t.orders.searchPlaceholder}
                className="pl-9"
              />
            </div>
            <Button
              variant={overdue ? 'default' : 'outline'}
              aria-pressed={overdue}
              onClick={() => update({ overdue: overdue ? null : '1' })}
              className="self-start"
            >
              <AlarmClock />
              {t.orders.overdueOnly}
              {summary.data && summary.data.overdue > 0 && <span className="tabular-nums">· {summary.data.overdue}</span>}
            </Button>
          </div>

          {/* Status chips */}
          <div role="group" aria-label={t.orders.statusLabel} className="-mx-1 flex [scrollbar-width:none] gap-1.5 overflow-x-auto px-1 pb-1">
            <StatusChip active={status === 'open'} onClick={() => update({ status: null })} label={t.orders.openStatuses} count={openCount} />
            {ORDER_STATUSES.map((s) => (
              <StatusChip
                key={s}
                active={status === s}
                onClick={() => update({ status: s })}
                label={t.orderStatus[s]}
                count={counts.get(s)}
              />
            ))}
            <StatusChip active={status === 'all'} onClick={() => update({ status: 'all' })} label={t.orders.allStatuses} />
          </div>
        </div>

        {orders.isError ? (
          <div className="px-6 py-14 text-center">
            <p className="text-sm text-muted-foreground">{t.orders.loadError}</p>
            <Button variant="outline" className="mt-4" onClick={() => void orders.refetch()}>
              {t.common.retry}
            </Button>
          </div>
        ) : (
          <OrdersTable
            orders={orders.data?.items}
            fetching={orders.isFetching}
            emptyText={scope === 'mine' && !search && status === 'open' ? t.orders.emptyMine : t.orders.empty}
          />
        )}
        {orders.data && <Pager data={orders.data} onPage={(p) => update({ page: String(p) })} />}
      </section>
    </div>
  )
}

function StatusChip({ active, onClick, label, count }: { active: boolean; onClick: () => void; label: string; count?: number }) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={cn(
        'inline-flex h-8 shrink-0 items-center gap-1.5 rounded-full border px-3 text-sm whitespace-nowrap transition-colors',
        active ? 'border-primary bg-accent font-medium text-accent-foreground' : 'bg-card text-muted-foreground hover:text-foreground',
      )}
    >
      {label}
      {count !== undefined && <span className="text-xs tabular-nums opacity-70">{count}</span>}
    </button>
  )
}
