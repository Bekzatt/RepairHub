import { useEffect, useState, type ReactNode } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router'
import { ArrowLeft, ExternalLink, Hand, Lock, Pencil } from 'lucide-react'
import { Notice, useNotice } from '@/components/notice'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { FRONT_OFFICE_ROLES } from '@/config/navigation'
import { useCurrentUser } from '@/features/auth/auth-store'
import {
  useAssignees,
  useAssignTechnician,
  useChangeStatus,
  useOrder,
  useTakeOrder,
  useUpdateDiagnostics,
} from '@/features/repair-orders/api'
import {
  IntakeDialog,
  orderErrorMessage,
  SnapshotDialog,
  StatusDialog,
  TermsDialog,
} from '@/features/repair-orders/order-dialogs'
import { OrderHistory } from '@/features/repair-orders/order-history'
import { OutcomeLabel, StatusBadge } from '@/features/repair-orders/status-badge'
import type { AvailableTransition, OrderDetails } from '@/features/repair-orders/types'
import { ApiError } from '@/lib/api-client'
import { formatCalendarDate, formatDateTime, formatMoney } from '@/lib/format'
import { cn } from '@/lib/utils'
import { t } from '@/locales'

const UNASSIGNED = '__none__'

export function OrderDetailsPage() {
  const { id } = useParams()
  const location = useLocation()
  const order = useOrder(id)
  const notice = useNotice()
  const navigate = useNavigate()
  // "Order RH-… created" is shown once: keep it for this visit, then drop it from history so a reload doesn't repeat it.
  const [createdMessage, setCreatedMessage] = useState(() => (location.state as { created?: string } | null)?.created)
  useEffect(() => {
    if (location.state) navigate(location.pathname, { replace: true, state: null })
  }, [location.state, location.pathname, navigate])
  useEffect(() => {
    if (!createdMessage) return
    const timer = setTimeout(() => setCreatedMessage(undefined), 5000)
    return () => clearTimeout(timer)
  }, [createdMessage])

  if (order.isError) {
    const missing = order.error instanceof ApiError && (order.error.status === 404 || order.error.status === 400)
    return (
      <div className="mx-auto max-w-md py-16 text-center">
        <p className="text-sm text-muted-foreground">{missing ? t.order.notFound : t.common.loadError}</p>
        <Button asChild variant="outline" className="mt-4">
          <Link to="/orders">{t.order.back}</Link>
        </Button>
      </div>
    )
  }
  if (!order.data) return <DetailsSkeleton />

  return (
    <OrderView
      order={order.data}
      notice={notice}
      createdMessage={notice.flash || notice.error ? undefined : createdMessage}
    />
  )
}

function OrderView({
  order,
  notice,
  createdMessage,
}: {
  order: OrderDetails
  notice: ReturnType<typeof useNotice>
  createdMessage?: string
}) {
  const user = useCurrentUser()
  const isFrontOffice = FRONT_OFFICE_ROLES.includes(user.role)
  const [dialog, setDialog] = useState<'intake' | 'terms' | 'snapshot' | null>(null)
  const [transition, setTransition] = useState<AvailableTransition | null>(null)
  const changeStatus = useChangeStatus(order.id)
  const closed = order.status === 'Issued' || order.status === 'Cancelled'

  async function runTransition(next: AvailableTransition) {
    if (next.requiresOutcome || next.requiresReason) {
      setTransition(next)
      return
    }
    try {
      await changeStatus.mutateAsync({ status: next.to, outcome: null, comment: null, version: order.version })
      notice.done(t.order.done.status(t.orderStatus[next.to]))
    } catch (error) {
      notice.fail(orderErrorMessage(error))
    }
  }

  const transitions = order.allowedTransitions.filter((tr) => tr.to !== 'Cancelled')
  const cancel = order.allowedTransitions.find((tr) => tr.to === 'Cancelled')

  return (
    <div className="space-y-6">
      <div>
        <Link to="/orders" className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" />
          {t.order.back}
        </Link>
        <div className="mt-3 flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-3">
              <h1 className="text-2xl font-semibold tracking-tight tabular-nums sm:text-[28px]">{order.number}</h1>
              <StatusBadge status={order.status} />
              {order.outcome && <OutcomeLabel outcome={order.outcome} />}
            </div>
            <p className="mt-1 truncate text-sm text-muted-foreground">
              {t.deviceTypes[order.deviceType]} {order.deviceBrand} {order.deviceModel} · {order.customerName}
            </p>
          </div>

          {/* Status actions: exactly the transitions the server allows this user right now. */}
          {order.allowedTransitions.length > 0 && (
            <div role="group" aria-label={t.order.statusActions} className="flex flex-wrap gap-2 lg:justify-end">
              {transitions.map((tr, index) => (
                <Button
                  key={tr.to}
                  variant={index === 0 ? 'default' : 'outline'}
                  disabled={changeStatus.isPending}
                  onClick={() => void runTransition(tr)}
                >
                  {t.orderAction[tr.to]}
                </Button>
              ))}
              {cancel && (
                <Button variant="ghost" className="text-destructive hover:text-destructive" onClick={() => setTransition(cancel)}>
                  {t.orderAction.Cancelled}
                </Button>
              )}
            </div>
          )}
        </div>
      </div>

      <Notice flash={notice.flash ?? createdMessage ?? null} error={notice.error} />
      {closed && (
        <p className="flex items-center gap-2 rounded-md bg-secondary px-3 py-2 text-sm text-muted-foreground">
          <Lock className="size-4" />
          {t.order.closedNotice}
        </p>
      )}

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_340px]">
        <div className="min-w-0 space-y-6">
          <Card
            title={t.order.sections.intake}
            action={order.can.editIntake && <EditButton label={t.order.editIntake} onClick={() => setDialog('intake')} />}
          >
            <dl className="space-y-4">
              <Field label={t.order.problem}>
                <p className="whitespace-pre-wrap">{order.problem}</p>
              </Field>
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label={t.order.condition}>{order.condition ?? <Muted>{t.common.notSet}</Muted>}</Field>
                <Field label={t.order.accessories}>{order.accessories ?? <Muted>{t.common.notSet}</Muted>}</Field>
              </div>
            </dl>
          </Card>

          <DiagnosticsCard order={order} onDone={notice.done} />

          <OrderHistory orderId={order.id} currency={order.currency} canAddNote={order.can.addNote} />
        </div>

        <aside className="space-y-6">
          <TechnicianCard order={order} notice={notice} />

          <Card
            title={t.order.sections.terms}
            action={order.can.editTerms && <EditButton compact label={t.order.editTerms} onClick={() => setDialog('terms')} />}
          >
            <dl className="space-y-3 text-sm">
              <Row label={t.order.estimatedPrice}>
                {order.estimatedPrice !== null ? formatMoney(order.estimatedPrice, order.currency) : <Muted>{t.common.notSet}</Muted>}
              </Row>
              <Row label={t.order.finalPrice}>
                {order.finalPrice !== null ? formatMoney(order.finalPrice, order.currency) : <Muted>{t.common.notSet}</Muted>}
              </Row>
              <Row label={t.order.expected}>
                {order.expectedCompletionDate ? (
                  <span className={cn(order.isOverdue && 'font-medium text-destructive')}>
                    {formatCalendarDate(order.expectedCompletionDate, { year: 'numeric' })}
                    {order.isOverdue && ` · ${t.order.overdue}`}
                  </span>
                ) : (
                  <Muted>{t.common.notSet}</Muted>
                )}
              </Row>
            </dl>
          </Card>

          <Card
            title={`${t.order.sections.customer} · ${t.order.sections.device}`}
            action={order.can.correctSnapshot && <EditButton compact label={t.order.correctSnapshot} onClick={() => setDialog('snapshot')} />}
          >
            <div className="space-y-4 text-sm">
              <div>
                <p className="font-medium">{order.customerName}</p>
                <a href={`tel:${order.customerPhone.replace(/[^\d+]/g, '')}`} className="text-primary tabular-nums hover:underline">
                  {order.customerPhone}
                </a>
                {isFrontOffice && (
                  <SmallLink to={`/customers/${order.customerId}`}>{t.order.openCustomer}</SmallLink>
                )}
              </div>
              <div className="border-t pt-4">
                <p className="font-medium">
                  {order.deviceBrand} {order.deviceModel}
                </p>
                <p className="text-muted-foreground">{t.deviceTypes[order.deviceType]}</p>
                {order.deviceSerialNumber && (
                  <p className="text-muted-foreground">
                    {t.order.serial}: <span className="font-mono text-foreground">{order.deviceSerialNumber}</span>
                  </p>
                )}
                {order.deviceImei && (
                  <p className="text-muted-foreground">
                    {t.order.imei}: <span className="font-mono text-foreground">{order.deviceImei}</span>
                  </p>
                )}
                {isFrontOffice && <SmallLink to={`/devices/${order.deviceId}`}>{t.order.openDevice}</SmallLink>}
              </div>
            </div>
          </Card>

          <DatesCard order={order} />
        </aside>
      </div>

      <IntakeDialog order={order} open={dialog === 'intake'} onOpenChange={(o) => !o && setDialog(null)} onDone={notice.done} />
      <TermsDialog order={order} open={dialog === 'terms'} onOpenChange={(o) => !o && setDialog(null)} onDone={notice.done} />
      <SnapshotDialog order={order} open={dialog === 'snapshot'} onOpenChange={(o) => !o && setDialog(null)} onDone={notice.done} />
      <StatusDialog order={order} transition={transition} onClose={() => setTransition(null)} onDone={notice.done} />
    </div>
  )
}

function DiagnosticsCard({ order, onDone }: { order: OrderDetails; onDone: (m: string) => void }) {
  const update = useUpdateDiagnostics(order.id)
  const [editing, setEditing] = useState(false)
  const [text, setText] = useState('')
  const [error, setError] = useState<string | null>(null)

  function start() {
    setText(order.diagnostics ?? '')
    setError(null)
    setEditing(true)
  }

  async function save() {
    setError(null)
    try {
      await update.mutateAsync({ diagnostics: text.trim() || null, version: order.version })
      setEditing(false)
      onDone(t.order.done.saved)
    } catch (e) {
      setError(orderErrorMessage(e))
    }
  }

  return (
    <Card
      title={t.order.sections.diagnostics}
      action={
        order.can.editDiagnostics &&
        !editing && (
          <EditButton label={order.diagnostics ? t.order.editDiagnostics : t.order.addDiagnostics} onClick={start} />
        )
      }
    >
      {editing ? (
        <div className="space-y-3">
          {error && (
            <p role="alert" className="text-sm text-destructive">
              {error}
            </p>
          )}
          <Textarea
            value={text}
            onChange={(e) => setText(e.target.value)}
            maxLength={4000}
            rows={5}
            autoFocus
            placeholder={t.order.diagnosticsPlaceholder}
            aria-label={t.order.sections.diagnostics}
          />
          <div className="flex justify-end gap-2">
            <Button variant="outline" size="sm" onClick={() => setEditing(false)}>
              {t.common.cancel}
            </Button>
            <Button size="sm" disabled={update.isPending} onClick={() => void save()}>
              {update.isPending ? t.common.saving : t.common.save}
            </Button>
          </div>
        </div>
      ) : order.diagnostics ? (
        <p className="text-sm whitespace-pre-wrap">{order.diagnostics}</p>
      ) : (
        <p className="text-sm text-muted-foreground">{t.order.diagnosticsEmpty}</p>
      )}
    </Card>
  )
}

function TechnicianCard({ order, notice }: { order: OrderDetails; notice: ReturnType<typeof useNotice> }) {
  const take = useTakeOrder(order.id)
  const assign = useAssignTechnician(order.id)
  const assignees = useAssignees()

  async function onTake() {
    try {
      await take.mutateAsync(undefined)
      notice.done(t.order.done.taken)
    } catch (error) {
      notice.fail(orderErrorMessage(error))
    }
  }

  async function onAssign(value: string) {
    const technicianId = value === UNASSIGNED ? null : value
    if (technicianId === (order.assignedTechnician?.id ?? null)) return
    try {
      await assign.mutateAsync({ technicianId, version: order.version })
      notice.done(t.order.done.assigned)
    } catch (error) {
      notice.fail(orderErrorMessage(error))
    }
  }

  return (
    <Card title={t.order.sections.technician}>
      <div className="space-y-3 text-sm">
        {order.assignedTechnician ? (
          <p className="font-medium">{order.assignedTechnician.fullName}</p>
        ) : (
          <p className="text-muted-foreground">{t.order.noTechnician}</p>
        )}
        {order.can.take && (
          <Button className="w-full" disabled={take.isPending} onClick={() => void onTake()}>
            <Hand />
            {t.order.take}
          </Button>
        )}
        {order.can.assign && (
          <Select value={order.assignedTechnician?.id ?? UNASSIGNED} onValueChange={(v) => void onAssign(v)} disabled={assign.isPending}>
            <SelectTrigger className="w-full" aria-label={order.assignedTechnician ? t.order.changeTechnician : t.order.assign}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={UNASSIGNED}>{t.order.unassign}</SelectItem>
              {/* Keep the current technician selectable even if they were deactivated since. */}
              {order.assignedTechnician && !assignees.data?.some((a) => a.id === order.assignedTechnician?.id) && (
                <SelectItem value={order.assignedTechnician.id}>{order.assignedTechnician.fullName}</SelectItem>
              )}
              {assignees.data?.map((a) => (
                <SelectItem key={a.id} value={a.id}>
                  {a.fullName} · {t.roles[a.role]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        )}
      </div>
    </Card>
  )
}

function DatesCard({ order }: { order: OrderDetails }) {
  const { workshop } = useCurrentUser()
  const rows: [string, string | null][] = [
    [t.order.dates.receivedAt, order.receivedAt],
    [t.order.dates.startedAt, order.startedAt],
    [t.order.dates.completedAt, order.completedAt],
    [t.order.dates.issuedAt, order.issuedAt],
    [t.order.dates.cancelledAt, order.cancelledAt],
  ]
  return (
    <Card title={t.order.sections.dates}>
      <dl className="space-y-2 text-sm">
        {rows
          .filter(([, value]) => value)
          .map(([label, value]) => (
            <Row key={label} label={label}>
              <span className="tabular-nums">{formatDateTime(value!, workshop.timeZone)}</span>
            </Row>
          ))}
        <p className="pt-1 text-xs text-muted-foreground">{t.order.receivedBy(order.createdBy.fullName)}</p>
        {order.cancellationReason && (
          <Row label={t.order.cancellationReason}>
            <span className="whitespace-pre-wrap">{order.cancellationReason}</span>
          </Row>
        )}
      </dl>
    </Card>
  )
}

/* ------------------------------------------------------------------ small building blocks */

function Card({ title, action, children }: { title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <section className="rounded-xl border bg-card p-5">
      <div className="mb-3 flex items-center justify-between gap-3">
        <h2 className="text-[15px] font-semibold">{title}</h2>
        {action}
      </div>
      {children}
    </section>
  )
}

/** Text button in the main column; icon-only (with tooltip) in the narrow side column. */
function EditButton({ label, onClick, compact }: { label: string; onClick: () => void; compact?: boolean }) {
  return (
    <Button
      variant="ghost"
      size={compact ? 'icon-sm' : 'sm'}
      onClick={onClick}
      title={label}
      aria-label={label}
      className="-mr-2 shrink-0 text-muted-foreground"
    >
      <Pencil />
      {!compact && <span className="hidden sm:inline">{label}</span>}
    </Button>
  )
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="text-sm">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-1">{children}</dd>
    </div>
  )
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-4">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right">{children}</dd>
    </div>
  )
}

function Muted({ children }: { children: ReactNode }) {
  return <span className="text-muted-foreground">{children}</span>
}

function SmallLink({ to, children }: { to: string; children: ReactNode }) {
  return (
    <Link to={to} className="mt-1 flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground">
      {children}
      <ExternalLink className="size-3" />
    </Link>
  )
}

function DetailsSkeleton() {
  return (
    <div className="space-y-6" role="status" aria-live="polite">
      <span className="sr-only">{t.status.loading}</span>
      <span className="block h-8 w-64 animate-pulse rounded bg-muted" />
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_340px]">
        <span className="block h-64 animate-pulse rounded-xl bg-muted" />
        <span className="block h-64 animate-pulse rounded-xl bg-muted" />
      </div>
    </div>
  )
}
