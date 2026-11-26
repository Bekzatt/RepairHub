import { Link, useNavigate } from 'react-router'
import { ClipboardList } from 'lucide-react'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useCurrentUser } from '@/features/auth/auth-store'
import { formatCalendarDate, formatDate } from '@/lib/format'
import { cn } from '@/lib/utils'
import { t } from '@/locales'
import { OutcomeLabel, StatusBadge } from './status-badge'
import type { OrderListItem } from './types'

interface OrdersTableProps {
  orders: OrderListItem[] | undefined
  fetching?: boolean
  emptyText: string
  /** Hide the customer column (customer page). */
  hideCustomer?: boolean
  /** Hide the device column (device page). */
  hideDevice?: boolean
}

/** Repair orders as a table on wide screens and as cards on phones. Rows open the order. */
export function OrdersTable({ orders, fetching, emptyText, hideCustomer, hideDevice }: OrdersTableProps) {
  const navigate = useNavigate()
  const { workshop } = useCurrentUser()

  if (orders && orders.length === 0) {
    return (
      <div className="flex flex-col items-center px-6 py-14 text-center">
        <ClipboardList className="mb-3 size-6 text-muted-foreground" strokeWidth={1.75} />
        <p className="max-w-sm text-sm text-muted-foreground">{emptyText}</p>
      </div>
    )
  }

  return (
    <div className={cn('transition-opacity', fetching && orders && 'opacity-60')}>
      {/* Phones: cards */}
      <ul className="divide-y md:hidden">
        {!orders
          ? Array.from({ length: 4 }, (_, i) => (
              <li key={i} className="space-y-2 px-4 py-3">
                <span className="block h-4 w-2/3 animate-pulse rounded bg-muted" />
                <span className="block h-3 w-1/2 animate-pulse rounded bg-muted" />
              </li>
            ))
          : orders.map((order) => (
              <li key={order.id}>
                <Link to={`/orders/${order.id}`} className="block px-4 py-3 hover:bg-muted/50 focus-visible:bg-muted/50 focus-visible:outline-none">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <p className="text-xs font-medium text-muted-foreground tabular-nums">{order.number}</p>
                      <p className="truncate font-medium">
                        {order.deviceBrand} {order.deviceModel}
                      </p>
                      {!hideCustomer && <p className="truncate text-sm text-muted-foreground">{order.customerName}</p>}
                    </div>
                    <StatusBadge status={order.status} />
                  </div>
                  <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
                    <span>{order.assignedTechnician?.fullName ?? t.orders.noTechnician}</span>
                    {order.expectedCompletionDate && (
                      <span className={cn(order.isOverdue && 'font-medium text-destructive')}>
                        {t.orders.columns.expected}: {formatCalendarDate(order.expectedCompletionDate)}
                      </span>
                    )}
                  </div>
                </Link>
              </li>
            ))}
      </ul>

      {/* Wider screens: table */}
      <div className="hidden overflow-x-auto md:block">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="pl-4">{t.orders.columns.number}</TableHead>
              {!hideDevice && <TableHead>{t.orders.columns.device}</TableHead>}
              {!hideCustomer && <TableHead>{t.orders.columns.customer}</TableHead>}
              <TableHead>{t.orders.columns.status}</TableHead>
              <TableHead className="hidden lg:table-cell">{t.orders.columns.technician}</TableHead>
              <TableHead>{t.orders.columns.expected}</TableHead>
              <TableHead className="hidden pr-4 xl:table-cell">{t.orders.columns.received}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {!orders
              ? Array.from({ length: 5 }, (_, i) => (
                  <TableRow key={i}>
                    <TableCell colSpan={7} className="pl-4">
                      <span className="block h-4 w-full animate-pulse rounded bg-muted" />
                    </TableCell>
                  </TableRow>
                ))
              : orders.map((order) => (
                  <TableRow
                    key={order.id}
                    className="cursor-pointer"
                    onClick={() => navigate(`/orders/${order.id}`)}
                  >
                    <TableCell className="pl-4 align-top">
                      <Link
                        to={`/orders/${order.id}`}
                        onClick={(e) => e.stopPropagation()}
                        className="font-medium tabular-nums hover:underline"
                      >
                        {order.number}
                      </Link>
                    </TableCell>
                    {!hideDevice && (
                      <TableCell className="max-w-72 align-top">
                        <div className="truncate font-medium">
                          {order.deviceBrand} {order.deviceModel}
                        </div>
                        <div className="truncate text-xs text-muted-foreground">
                          {t.deviceTypes[order.deviceType]} · {order.problem}
                        </div>
                      </TableCell>
                    )}
                    {!hideCustomer && (
                      <TableCell className="max-w-56 align-top">
                        <div className="truncate">{order.customerName}</div>
                        <div className="text-xs text-muted-foreground tabular-nums">{order.customerPhone}</div>
                      </TableCell>
                    )}
                    <TableCell className="align-top">
                      <div className="flex flex-col items-start gap-1">
                        <StatusBadge status={order.status} />
                        {order.outcome && <OutcomeLabel outcome={order.outcome} />}
                      </div>
                    </TableCell>
                    <TableCell className="hidden align-top lg:table-cell">
                      {order.assignedTechnician?.fullName ?? (
                        <span className="text-muted-foreground">{t.orders.noTechnician}</span>
                      )}
                    </TableCell>
                    <TableCell className="align-top whitespace-nowrap">
                      {order.expectedCompletionDate ? (
                        <span className={cn(order.isOverdue && 'font-medium text-destructive')}>
                          {formatCalendarDate(order.expectedCompletionDate)}
                          {order.isOverdue && <span className="block text-xs font-normal">{t.order.overdue}</span>}
                        </span>
                      ) : (
                        <span className="text-muted-foreground">—</span>
                      )}
                    </TableCell>
                    <TableCell className="hidden pr-4 align-top text-muted-foreground tabular-nums xl:table-cell">
                      {formatDate(order.receivedAt, workshop.timeZone)}
                    </TableCell>
                  </TableRow>
                ))}
          </TableBody>
        </Table>
      </div>
    </div>
  )
}
