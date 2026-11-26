import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { Archive, ArchiveRestore, Pencil, Plus } from 'lucide-react'
import { Notice, useNotice } from '@/components/notice'
import { Pager } from '@/components/pager'
import { Button } from '@/components/ui/button'
import { MANAGER_ROLES } from '@/config/navigation'
import { useCurrentUser } from '@/features/auth/auth-store'
import { useDevice, useDeviceOrders, useSetDeviceArchived } from '@/features/devices/api'
import { DeviceFormDialog } from '@/features/devices/device-form-dialog'
import { OrdersTable } from '@/features/repair-orders/orders-table'
import { ApiError } from '@/lib/api-client'
import { t } from '@/locales'
import { BackLink, ConfirmDialog, Missing, PageSkeleton, Stat } from './customer-details-page'

export function DeviceDetailsPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const user = useCurrentUser()
  const details = useDevice(id)
  const [ordersPage, setOrdersPage] = useState(1)
  const orders = useDeviceOrders(id, ordersPage)
  const setArchived = useSetDeviceArchived()
  const notice = useNotice()
  const [editing, setEditing] = useState(false)
  const [confirmArchive, setConfirmArchive] = useState(false)

  if (details.isError) {
    const missing = details.error instanceof ApiError && details.error.status === 404
    return <Missing text={missing ? t.devices.profile.notFound : t.common.loadError} back="/devices" backLabel={t.devices.title} />
  }
  if (!details.data) return <PageSkeleton />

  const { device } = details.data
  const canArchive = MANAGER_ROLES.includes(user.role)

  async function archive(archived: boolean) {
    try {
      await setArchived.mutateAsync({ id: device.id, archived })
      setConfirmArchive(false)
      notice.done(archived ? t.devices.done.archived : t.devices.done.restored)
    } catch (error) {
      setConfirmArchive(false)
      notice.fail(error)
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <BackLink to="/devices" label={t.devices.title} />
        <div className="mt-3 flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div className="min-w-0">
            <p className="text-sm text-muted-foreground">{t.deviceTypes[device.type]}</p>
            <h1 className="flex flex-wrap items-center gap-3 text-2xl font-semibold tracking-tight sm:text-[28px]">
              {device.brand} {device.model}
              {device.isArchived && <span className="rounded-full bg-secondary px-2.5 py-0.5 text-sm font-medium">{t.common.archived}</span>}
            </h1>
            <dl className="mt-2 space-y-0.5 text-sm">
              <div>
                <dt className="inline text-muted-foreground">{t.devices.profile.owner}: </dt>
                <dd className="inline">
                  <Link to={`/customers/${device.customerId}`} className="text-primary hover:underline">
                    {device.customerName}
                  </Link>
                </dd>
              </div>
              {device.serialNumber && (
                <div>
                  <dt className="inline text-muted-foreground">{t.devices.form.serialNumber}: </dt>
                  <dd className="inline font-mono">{device.serialNumber}</dd>
                </div>
              )}
              {device.imei && (
                <div>
                  <dt className="inline text-muted-foreground">{t.devices.form.imei}: </dt>
                  <dd className="inline font-mono">{device.imei}</dd>
                </div>
              )}
              {device.description && <dd className="text-muted-foreground">{device.description}</dd>}
            </dl>
          </div>
          <div className="flex flex-wrap gap-2">
            {!device.isArchived && (
              <Button onClick={() => navigate(`/orders/new?customerId=${device.customerId}&deviceId=${device.id}`)}>
                <Plus />
                {t.devices.profile.newOrder}
              </Button>
            )}
            <Button variant="outline" onClick={() => setEditing(true)}>
              <Pencil />
              {t.common.edit}
            </Button>
            {canArchive &&
              (device.isArchived ? (
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

      <Notice flash={notice.flash} error={notice.error} />
      {device.isArchived && <p className="rounded-md bg-secondary px-3 py-2 text-sm text-muted-foreground">{t.devices.profile.archivedNotice}</p>}

      <dl className="grid grid-cols-2 gap-3 sm:max-w-xs">
        <Stat label={t.devices.profile.repairs} value={details.data.orderCount} />
        <Stat label={t.customers.profile.openOrders} value={details.data.openOrderCount} />
      </dl>

      <section className="rounded-xl border bg-card">
        <h2 className="px-5 pt-5 pb-3 text-[15px] font-semibold">{t.devices.profile.repairs}</h2>
        <OrdersTable orders={orders.data?.items} fetching={orders.isFetching} emptyText={t.devices.profile.noOrders} hideDevice />
        {orders.data && <Pager data={orders.data} onPage={setOrdersPage} />}
      </section>

      <DeviceFormDialog
        device={device}
        customerId={device.customerId}
        open={editing}
        onOpenChange={setEditing}
        onSaved={() => notice.done(t.devices.done.updated)}
      />
      <ConfirmDialog
        open={confirmArchive}
        onOpenChange={setConfirmArchive}
        title={t.devices.archiveDialog.title}
        text={t.devices.archiveDialog.text}
        confirm={t.common.archive}
        pending={setArchived.isPending}
        onConfirm={() => void archive(true)}
      />
    </div>
  )
}
