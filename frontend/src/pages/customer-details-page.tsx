import { useState, type ReactNode } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router'
import { Archive, ArchiveRestore, ArrowLeft, Laptop, Pencil, Plus } from 'lucide-react'
import { Notice, useNotice } from '@/components/notice'
import { Pager } from '@/components/pager'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { MANAGER_ROLES } from '@/config/navigation'
import { useCurrentUser } from '@/features/auth/auth-store'
import { useCustomer, useCustomerDevices, useCustomerOrders, useSetCustomerArchived } from '@/features/customers/api'
import { CustomerFormDialog } from '@/features/customers/customer-form-dialog'
import { DeviceFormDialog } from '@/features/devices/device-form-dialog'
import { OrdersTable } from '@/features/repair-orders/orders-table'
import { ApiError } from '@/lib/api-client'
import { formatDate } from '@/lib/format'
import { cn } from '@/lib/utils'
import { t } from '@/locales'

export function CustomerDetailsPage() {
  const { id } = useParams()
  const location = useLocation()
  const navigate = useNavigate()
  const user = useCurrentUser()
  const details = useCustomer(id)
  const devices = useCustomerDevices(id)
  const [ordersPage, setOrdersPage] = useState(1)
  const orders = useCustomerOrders(id, ordersPage)
  const setArchived = useSetCustomerArchived()
  const notice = useNotice()
  const [editing, setEditing] = useState(false)
  const [addingDevice, setAddingDevice] = useState(false)
  const [confirmArchive, setConfirmArchive] = useState(false)
  const initialFlash = (location.state as { flash?: string } | null)?.flash

  if (details.isError) {
    const missing = details.error instanceof ApiError && details.error.status === 404
    return <Missing text={missing ? t.customers.profile.notFound : t.common.loadError} back="/customers" backLabel={t.customers.title} />
  }
  if (!details.data) return <PageSkeleton />

  const { customer } = details.data
  const canArchive = MANAGER_ROLES.includes(user.role)

  async function archive(archived: boolean) {
    try {
      await setArchived.mutateAsync({ id: customer.id, archived })
      setConfirmArchive(false)
      notice.done(archived ? t.customers.done.archived : t.customers.done.restored)
    } catch (error) {
      setConfirmArchive(false)
      notice.fail(error)
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <BackLink to="/customers" label={t.customers.title} />
        <div className="mt-3 flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div className="min-w-0">
            <h1 className="flex flex-wrap items-center gap-3 text-2xl font-semibold tracking-tight sm:text-[28px]">
              {customer.fullName}
              {customer.isArchived && <span className="rounded-full bg-secondary px-2.5 py-0.5 text-sm font-medium">{t.common.archived}</span>}
            </h1>
            <p className="mt-1 text-sm tabular-nums">
              <a href={`tel:${customer.phone.replace(/[^\d+]/g, '')}`} className="text-primary hover:underline">
                {customer.phone}
              </a>
              {customer.additionalPhone && <span className="text-muted-foreground"> · {customer.additionalPhone}</span>}
              {customer.email && <span className="text-muted-foreground"> · {customer.email}</span>}
            </p>
            {customer.notes && <p className="mt-2 max-w-prose text-sm whitespace-pre-wrap text-muted-foreground">{customer.notes}</p>}
          </div>
          <div className="flex flex-wrap gap-2">
            {!customer.isArchived && (
              <Button onClick={() => navigate(`/orders/new?customerId=${customer.id}`)}>
                <Plus />
                {t.customers.profile.newOrder}
              </Button>
            )}
            <Button variant="outline" onClick={() => setEditing(true)}>
              <Pencil />
              {t.common.edit}
            </Button>
            {canArchive &&
              (customer.isArchived ? (
                <Button variant="outline" onClick={() => void archive(false)} disabled={setArchived.isPending}>
                  <ArchiveRestore />
                  {t.common.restore}
                </Button>
              ) : (
                <Button variant="ghost" onClick={() => setConfirmArchive(true)}>
                  <Archive />
                  {t.common.archive}
                </Button>
              ))}
          </div>
        </div>
      </div>

      <Notice flash={notice.flash ?? (notice.error ? null : (initialFlash ?? null))} error={notice.error} />
      {customer.isArchived && <p className="rounded-md bg-secondary px-3 py-2 text-sm text-muted-foreground">{t.customers.profile.archivedNotice}</p>}

      <dl className="grid grid-cols-3 gap-3 sm:max-w-md">
        <Stat label={t.customers.profile.devices} value={details.data.deviceCount} />
        <Stat label={t.customers.profile.orders} value={details.data.orderCount} />
        <Stat label={t.customers.profile.openOrders} value={details.data.openOrderCount} />
      </dl>

      <section className="rounded-xl border bg-card">
        <div className="flex items-center justify-between gap-3 px-5 pt-5 pb-3">
          <h2 className="text-[15px] font-semibold">{t.customers.profile.devices}</h2>
          {!customer.isArchived && (
            <Button variant="outline" size="sm" onClick={() => setAddingDevice(true)}>
              <Plus />
              {t.customers.profile.addDevice}
            </Button>
          )}
        </div>
        {devices.data && devices.data.length === 0 ? (
          <p className="px-5 pb-5 text-sm text-muted-foreground">{t.customers.profile.noDevices}</p>
        ) : (
          <ul className="grid gap-2 px-5 pb-5 sm:grid-cols-2 xl:grid-cols-3">
            {(devices.data ?? []).map((device) => (
              <li key={device.id}>
                <Link
                  to={`/devices/${device.id}`}
                  className={cn(
                    'flex items-start gap-3 rounded-lg border px-3 py-2.5 text-sm transition-colors hover:bg-muted/50',
                    device.isArchived && 'opacity-60',
                  )}
                >
                  <Laptop className="mt-0.5 size-4 shrink-0 text-muted-foreground" strokeWidth={1.75} />
                  <span className="min-w-0">
                    <span className="block truncate font-medium">
                      {device.brand} {device.model}
                    </span>
                    <span className="block truncate text-xs text-muted-foreground">
                      {t.deviceTypes[device.type]}
                      {device.serialNumber && ` · ${device.serialNumber}`}
                    </span>
                    <span className="block text-xs text-muted-foreground">
                      {device.lastReceivedAt
                        ? t.newOrder.lastRepair(formatDate(device.lastReceivedAt, user.workshop.timeZone))
                        : t.newOrder.neverRepaired}
                    </span>
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="rounded-xl border bg-card">
        <h2 className="px-5 pt-5 pb-3 text-[15px] font-semibold">{t.customers.profile.orders}</h2>
        <OrdersTable orders={orders.data?.items} fetching={orders.isFetching} emptyText={t.customers.profile.noOrders} hideCustomer />
        {orders.data && <Pager data={orders.data} onPage={setOrdersPage} />}
      </section>

      <CustomerFormDialog
        customer={customer}
        open={editing}
        onOpenChange={setEditing}
        onSaved={() => notice.done(t.customers.done.updated)}
      />
      <DeviceFormDialog
        device={null}
        customerId={customer.id}
        customerName={customer.fullName}
        open={addingDevice}
        onOpenChange={setAddingDevice}
        onSaved={() => notice.done(t.devices.done.created)}
      />
      <ConfirmDialog
        open={confirmArchive}
        onOpenChange={setConfirmArchive}
        title={t.customers.archiveDialog.title}
        text={t.customers.archiveDialog.text}
        confirm={t.common.archive}
        pending={setArchived.isPending}
        onConfirm={() => void archive(true)}
      />
    </div>
  )
}

export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  text,
  confirm,
  pending,
  onConfirm,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  text: string
  confirm: string
  pending: boolean
  onConfirm: () => void
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{text}</DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t.common.cancel}
          </Button>
          <Button onClick={onConfirm} disabled={pending}>
            {confirm}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function BackLink({ to, label }: { to: string; label: string }) {
  return (
    <Link to={to} className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground">
      <ArrowLeft className="size-4" />
      {label}
    </Link>
  )
}

export function Stat({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="rounded-lg border bg-card px-3 py-2.5">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-xl font-semibold tabular-nums">{value}</dd>
    </div>
  )
}

export function Missing({ text, back, backLabel }: { text: string; back: string; backLabel: string }) {
  return (
    <div className="mx-auto max-w-md py-16 text-center">
      <p className="text-sm text-muted-foreground">{text}</p>
      <Button asChild variant="outline" className="mt-4">
        <Link to={back}>{backLabel}</Link>
      </Button>
    </div>
  )
}

export function PageSkeleton() {
  return (
    <div className="space-y-6" role="status" aria-live="polite">
      <span className="sr-only">{t.status.loading}</span>
      <span className="block h-8 w-64 animate-pulse rounded bg-muted" />
      <span className="block h-40 animate-pulse rounded-xl bg-muted" />
      <span className="block h-64 animate-pulse rounded-xl bg-muted" />
    </div>
  )
}
