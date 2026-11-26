import { useState } from 'react'
import {
  ArrowRightLeft,
  ClipboardCheck,
  FilePlus2,
  MessageSquare,
  Pencil,
  Stethoscope,
  UserRound,
  type LucideIcon,
} from 'lucide-react'
import { FormAlert } from '@/components/form-field'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { useCurrentUser } from '@/features/auth/auth-store'
import { errorMessage } from '@/lib/form-errors'
import { formatCalendarDate, formatDateTime, formatMoney } from '@/lib/format'
import { cn } from '@/lib/utils'
import { t } from '@/locales'
import { useAddNote, useOrderHistory } from './api'
import type { DeviceType, OrderEvent, OrderEventType, OrderStatus, RepairOutcome } from './types'

const icons: Partial<Record<OrderEventType, LucideIcon>> = {
  Created: FilePlus2,
  StatusChanged: ArrowRightLeft,
  OutcomeRecorded: ClipboardCheck,
  TechnicianAssigned: UserRound,
  DiagnosticsUpdated: Stethoscope,
  NoteAdded: MessageSquare,
}

/** The order's append-only timeline, newest first, with a box for adding notes. */
export function OrderHistory({ orderId, currency, canAddNote }: { orderId: string; currency: string; canAddNote: boolean }) {
  const history = useOrderHistory(orderId)
  const events = history.data ? [...history.data].reverse() : undefined

  return (
    <section aria-labelledby="history-title" className="rounded-xl border bg-card">
      <h2 id="history-title" className="px-5 pt-5 text-[15px] font-semibold">
        {t.history.title}
      </h2>
      {canAddNote && <NoteComposer orderId={orderId} />}
      {history.isError ? (
        <div className="px-5 py-8 text-center text-sm text-muted-foreground">
          {t.common.loadError}{' '}
          <Button variant="link" className="px-1" onClick={() => void history.refetch()}>
            {t.common.retry}
          </Button>
        </div>
      ) : !events ? (
        <div className="space-y-3 p-5">
          {Array.from({ length: 3 }, (_, i) => (
            <span key={i} className="block h-10 animate-pulse rounded bg-muted" />
          ))}
        </div>
      ) : events.length === 0 ? (
        <p className="px-5 py-8 text-center text-sm text-muted-foreground">{t.history.empty}</p>
      ) : (
        <ol className="px-5 pt-2 pb-5">
          {events.map((event, index) => (
            <TimelineItem key={event.id} event={event} currency={currency} last={index === events.length - 1} />
          ))}
        </ol>
      )}
    </section>
  )
}

function NoteComposer({ orderId }: { orderId: string }) {
  const addNote = useAddNote(orderId)
  const [text, setText] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit() {
    if (!text.trim()) return
    setError(null)
    try {
      await addNote.mutateAsync(text.trim())
      setText('')
    } catch (e) {
      setError(errorMessage(e))
    }
  }

  return (
    <div className="space-y-2 px-5 pt-3 pb-2">
      {error && <FormAlert>{error}</FormAlert>}
      <Textarea
        value={text}
        onChange={(e) => setText(e.target.value)}
        maxLength={2000}
        rows={2}
        placeholder={t.history.notePlaceholder}
        aria-label={t.history.addNote}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) void submit()
        }}
      />
      <div className="flex justify-end">
        <Button size="sm" variant="outline" disabled={!text.trim() || addNote.isPending} onClick={() => void submit()}>
          <MessageSquare />
          {addNote.isPending ? t.history.adding : t.history.addNote}
        </Button>
      </div>
    </div>
  )
}

function TimelineItem({ event, currency, last }: { event: OrderEvent; currency: string; last: boolean }) {
  const { workshop } = useCurrentUser()
  const Icon = icons[event.type] ?? Pencil
  const isNote = event.type === 'NoteAdded'

  return (
    <li className="relative flex gap-3 pt-4">
      {!last && <span aria-hidden className="absolute top-11 bottom-[-16px] left-[15px] w-px bg-border" />}
      <span
        className={cn(
          'z-10 flex size-8 shrink-0 items-center justify-center rounded-full border bg-card',
          isNote ? 'text-warning' : 'text-muted-foreground',
          event.type === 'StatusChanged' && 'border-primary/40 text-primary',
        )}
      >
        <Icon className="size-4" strokeWidth={1.75} />
      </span>
      <div className="min-w-0 flex-1 pt-1">
        <p className="text-sm">
          <span className="font-medium">{t.history.events[event.type]}</span>
          <EventValue event={event} currency={currency} />
        </p>
        {event.comment && <p className="mt-1 text-sm whitespace-pre-wrap text-muted-foreground">«{event.comment}»</p>}
        <p className="mt-0.5 text-xs text-muted-foreground">
          {event.actor.fullName} · <time dateTime={event.occurredAt}>{formatDateTime(event.occurredAt, workshop.timeZone)}</time>
        </p>
      </div>
    </li>
  )
}

function EventValue({ event, currency }: { event: OrderEvent; currency: string }) {
  const none = t.history.noValue
  const change = (from: string, to: string) => (
    <>
      {': '}
      <span className="text-muted-foreground">{from}</span> → <span className="font-medium">{to}</span>
    </>
  )
  const block = (text: string | null) =>
    text ? <span className="mt-1 block rounded-md bg-muted/60 px-3 py-2 whitespace-pre-wrap">{text}</span> : null

  switch (event.type) {
    case 'Created':
      return event.newValue ? <span className="text-muted-foreground"> · {event.newValue}</span> : null
    case 'StatusChanged':
      return change(status(event.oldValue), status(event.newValue))
    case 'OutcomeRecorded':
      return <>: {event.newValue ? t.outcome[event.newValue as RepairOutcome] : none}</>
    case 'TechnicianAssigned':
      return change(event.oldValue ?? none, event.newValue ?? none)
    case 'EstimateChanged':
    case 'FinalPriceChanged':
      return change(money(event.oldValue, currency), money(event.newValue, currency))
    case 'ExpectedDateChanged':
      return change(date(event.oldValue), date(event.newValue))
    case 'CustomerInfoCorrected':
    case 'DeviceInfoCorrected':
      return <SnapshotChanges oldValue={event.oldValue} newValue={event.newValue} />
    case 'NoteAdded':
    case 'DiagnosticsUpdated':
    case 'ProblemUpdated':
    case 'ConditionUpdated':
    case 'AccessoriesUpdated':
      return block(event.newValue)
    default:
      return null
  }

  function status(value: string | null) {
    return value ? t.orderStatus[value as OrderStatus] : none
  }
  function money(value: string | null, cur: string) {
    return value ? formatMoney(Number(value), cur) : none
  }
  function date(value: string | null) {
    return value ? formatCalendarDate(value, { year: 'numeric' }) : none
  }
}

function SnapshotChanges({ oldValue, newValue }: { oldValue: string | null; newValue: string | null }) {
  const before = parse(oldValue)
  const after = parse(newValue)
  const fields = Object.keys(after) as (keyof typeof t.history.fields)[]
  const show = (field: string, value: string | null | undefined) =>
    !value ? t.history.noValue : field === 'type' ? t.deviceTypes[value as DeviceType] : value

  return (
    <span className="mt-1 block space-y-0.5 text-sm">
      {fields.map((field) => (
        <span key={field} className="block">
          <span className="text-muted-foreground">{t.history.fields[field] ?? field}: </span>
          <span className="text-muted-foreground line-through">{show(field, before[field])}</span> →{' '}
          <span className="font-medium">{show(field, after[field])}</span>
        </span>
      ))}
    </span>
  )
}

function parse(value: string | null): Record<string, string | null> {
  try {
    return value ? (JSON.parse(value) as Record<string, string | null>) : {}
  } catch {
    return {}
  }
}
