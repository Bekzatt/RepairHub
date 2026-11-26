import { useState } from 'react'
import { Controller, useForm, type UseFormRegisterReturn } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { FormAlert, FormField } from '@/components/form-field'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { useCurrentUser } from '@/features/auth/auth-store'
import { DeviceFields, deviceSchema, toDeviceInput } from '@/features/devices/device-fields'
import { ApiError } from '@/lib/api-client'
import { errorMessage } from '@/lib/form-errors'
import { addDays, todayIn } from '@/lib/format'
import { cn } from '@/lib/utils'
import { moneySchema, optionalText, orNull, parseMoney, phoneSchema, requiredText } from '@/lib/validation'
import { t } from '@/locales'
import { useChangeStatus, useCorrectSnapshot, useUpdateIntake, useUpdateTerms } from './api'
import type { AvailableTransition, OrderDetails, RepairOutcome } from './types'

/** Human message for a failed order change; a stale version gets the "someone else changed it" text. */
export function orderErrorMessage(error: unknown): string {
  return error instanceof ApiError && error.code === 'concurrency_conflict' ? t.order.conflict : errorMessage(error)
}

interface OrderDialogProps {
  order: OrderDetails
  open: boolean
  onOpenChange: (open: boolean) => void
  onDone: (message: string) => void
}

function OrderDialog({
  open,
  onOpenChange,
  title,
  description,
  wide,
  children,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: string
  wide?: boolean
  children: React.ReactNode
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className={cn(wide ? 'sm:max-w-2xl' : 'sm:max-w-lg')}>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {description && <DialogDescription>{description}</DialogDescription>}
        </DialogHeader>
        {open && children}
      </DialogContent>
    </Dialog>
  )
}

/* ------------------------------------------------------------------ status */

const statusSchema = z.object({ outcome: z.string(), comment: optionalText(500) })

/** For transitions that need an outcome (→ ReadyForPickup) or a reason (→ Cancelled). */
export function StatusDialog({
  order,
  transition,
  onClose,
  onDone,
}: {
  order: OrderDetails
  transition: AvailableTransition | null
  onClose: () => void
  onDone: (message: string) => void
}) {
  return (
    <OrderDialog
      open={transition !== null}
      onOpenChange={(open) => !open && onClose()}
      title={transition ? t.order.statusDialog.title(t.orderStatus[transition.to]) : ''}
      description={transition?.requiresReason ? t.order.statusDialog.cancelHint : undefined}
    >
      {transition && <StatusForm order={order} transition={transition} onClose={onClose} onDone={onDone} />}
    </OrderDialog>
  )
}

function StatusForm({
  order,
  transition,
  onClose,
  onDone,
}: {
  order: OrderDetails
  transition: AvailableTransition
  onClose: () => void
  onDone: (message: string) => void
}) {
  const changeStatus = useChangeStatus(order.id)
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<z.infer<typeof statusSchema>>({
    resolver: zodResolver(
      statusSchema.superRefine((v, ctx) => {
        if (transition.requiresOutcome && !v.outcome)
          ctx.addIssue({ code: 'custom', path: ['outcome'], message: t.validation.outcomeRequired })
        if (transition.requiresReason && !v.comment.trim())
          ctx.addIssue({ code: 'custom', path: ['comment'], message: t.validation.reasonRequired })
      }),
    ),
    defaultValues: { outcome: transition.outcomes.length === 1 ? transition.outcomes[0] : '', comment: '' },
  })
  const { errors, isSubmitting } = form.formState

  async function onSubmit(values: z.infer<typeof statusSchema>) {
    setFormError(null)
    try {
      await changeStatus.mutateAsync({
        status: transition.to,
        outcome: transition.requiresOutcome ? (values.outcome as RepairOutcome) : null,
        comment: orNull(values.comment),
        version: order.version,
      })
      onDone(t.order.done.status(t.orderStatus[transition.to]))
      onClose()
    } catch (error) {
      setFormError(orderErrorMessage(error))
    }
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-4">
      {formError && <FormAlert>{formError}</FormAlert>}
      {transition.requiresOutcome && (
        <fieldset className="space-y-2">
          <legend className="mb-2 text-sm font-medium">{t.order.statusDialog.outcome}</legend>
          <Controller
            control={form.control}
            name="outcome"
            render={({ field }) => (
              <div className="grid gap-2" role="radiogroup">
                {transition.outcomes.map((outcome) => (
                  <label
                    key={outcome}
                    className={cn(
                      'flex cursor-pointer items-center gap-3 rounded-lg border px-3 py-2.5 text-sm transition-colors',
                      field.value === outcome ? 'border-primary bg-accent' : 'hover:bg-muted/50',
                    )}
                  >
                    <input
                      type="radio"
                      name={field.name}
                      value={outcome}
                      checked={field.value === outcome}
                      onChange={() => field.onChange(outcome)}
                      className="accent-primary"
                    />
                    {t.outcome[outcome]}
                  </label>
                ))}
              </div>
            )}
          />
          {errors.outcome && <p className="text-xs text-destructive">{errors.outcome.message}</p>}
        </fieldset>
      )}
      <FormField
        label={transition.requiresReason ? t.order.statusDialog.reason : t.order.statusDialog.comment}
        optionalLabel={transition.requiresReason ? undefined : t.common.optional}
        error={errors.comment?.message}
      >
        {(p) => <Textarea {...p} rows={3} {...form.register('comment')} />}
      </FormField>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onClose}>
          {t.common.cancel}
        </Button>
        <Button type="submit" variant={transition.to === 'Cancelled' ? 'destructive' : 'default'} disabled={isSubmitting}>
          {transition.to === 'Cancelled' ? t.orderAction.Cancelled : t.order.statusDialog.confirm}
        </Button>
      </DialogFooter>
    </form>
  )
}

/* ------------------------------------------------------------------ intake */

const intakeSchema = z.object({
  problem: requiredText(2000),
  condition: optionalText(2000),
  accessories: optionalText(1000),
})

export function IntakeDialog({ order, open, onOpenChange, onDone }: OrderDialogProps) {
  return (
    <OrderDialog open={open} onOpenChange={onOpenChange} title={t.order.editIntake}>
      <IntakeForm order={order} onClose={() => onOpenChange(false)} onDone={onDone} />
    </OrderDialog>
  )
}

function IntakeForm({ order, onClose, onDone }: { order: OrderDetails; onClose: () => void; onDone: (m: string) => void }) {
  const update = useUpdateIntake(order.id)
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<z.infer<typeof intakeSchema>>({
    resolver: zodResolver(intakeSchema),
    defaultValues: { problem: order.problem, condition: order.condition ?? '', accessories: order.accessories ?? '' },
  })
  const { errors, isSubmitting } = form.formState

  async function onSubmit(values: z.infer<typeof intakeSchema>) {
    setFormError(null)
    try {
      await update.mutateAsync({
        problem: values.problem,
        condition: orNull(values.condition),
        accessories: orNull(values.accessories),
        version: order.version,
      })
      onDone(t.order.done.saved)
      onClose()
    } catch (error) {
      setFormError(orderErrorMessage(error))
    }
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-4">
      {formError && <FormAlert>{formError}</FormAlert>}
      <FormField label={t.order.problem} error={errors.problem?.message}>
        {(p) => <Textarea {...p} rows={3} {...form.register('problem')} />}
      </FormField>
      <FormField label={t.order.condition} optionalLabel={t.common.optional} error={errors.condition?.message}>
        {(p) => <Textarea {...p} rows={2} {...form.register('condition')} />}
      </FormField>
      <FormField label={t.order.accessories} optionalLabel={t.common.optional} error={errors.accessories?.message}>
        {(p) => <Input {...p} {...form.register('accessories')} />}
      </FormField>
      <SaveFooter onCancel={onClose} saving={isSubmitting} />
    </form>
  )
}

/* ------------------------------------------------------------------ terms */

export function TermsDialog({ order, open, onOpenChange, onDone }: OrderDialogProps) {
  return (
    <OrderDialog open={open} onOpenChange={onOpenChange} title={t.order.editTerms}>
      <TermsForm order={order} onClose={() => onOpenChange(false)} onDone={onDone} />
    </OrderDialog>
  )
}

function TermsForm({ order, onClose, onDone }: { order: OrderDetails; onClose: () => void; onDone: (m: string) => void }) {
  const { workshop } = useCurrentUser()
  const today = todayIn(workshop.timeZone)
  const update = useUpdateTerms(order.id)
  const [formError, setFormError] = useState<string | null>(null)
  const schema = z.object({
    estimatedPrice: moneySchema,
    finalPrice: moneySchema,
    // An unchanged past date may stay; a new date cannot be in the past.
    expectedCompletionDate: z
      .string()
      .refine((v) => v === '' || v === order.expectedCompletionDate || v >= today, { error: t.validation.datePast }),
  })
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: {
      estimatedPrice: order.estimatedPrice?.toString() ?? '',
      finalPrice: order.finalPrice?.toString() ?? '',
      expectedCompletionDate: order.expectedCompletionDate ?? '',
    },
  })
  const { errors, isSubmitting } = form.formState

  async function onSubmit(values: z.infer<typeof schema>) {
    setFormError(null)
    try {
      await update.mutateAsync({
        estimatedPrice: parseMoney(values.estimatedPrice),
        finalPrice: parseMoney(values.finalPrice),
        expectedCompletionDate: orNull(values.expectedCompletionDate),
        version: order.version,
      })
      onDone(t.order.done.saved)
      onClose()
    } catch (error) {
      setFormError(orderErrorMessage(error))
    }
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-4">
      {formError && <FormAlert>{formError}</FormAlert>}
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField label={`${t.order.estimatedPrice}, ${order.currency}`} optionalLabel={t.common.optional} error={errors.estimatedPrice?.message}>
          {(p) => <Input {...p} inputMode="decimal" {...form.register('estimatedPrice')} />}
        </FormField>
        <FormField label={`${t.order.finalPrice}, ${order.currency}`} optionalLabel={t.common.optional} error={errors.finalPrice?.message}>
          {(p) => <Input {...p} inputMode="decimal" {...form.register('finalPrice')} />}
        </FormField>
      </div>
      <ExpectedDateField
        value={form.watch('expectedCompletionDate')}
        onChange={(v) => form.setValue('expectedCompletionDate', v, { shouldValidate: true, shouldDirty: true })}
        today={today}
        error={errors.expectedCompletionDate?.message}
        register={form.register('expectedCompletionDate')}
      />
      <SaveFooter onCancel={onClose} saving={isSubmitting} />
    </form>
  )
}

/** Date input with quick choices (tomorrow, +3 days, +1 week) counted from today in the workshop time zone. */
export function ExpectedDateField({
  value,
  onChange,
  today,
  error,
  register,
}: {
  value: string
  onChange: (value: string) => void
  today: string
  error?: string
  register: UseFormRegisterReturn
}) {
  const quick = [
    { label: t.newOrder.quickDates.d1, days: 1 },
    { label: t.newOrder.quickDates.d3, days: 3 },
    { label: t.newOrder.quickDates.w1, days: 7 },
  ]
  return (
    <FormField label={t.order.expected} optionalLabel={t.common.optional} error={error}>
      {(p) => (
        <div className="flex flex-wrap items-center gap-2">
          <Input {...p} type="date" min={today} className="w-44" {...register} />
          {quick.map((q) => {
            const date = addDays(today, q.days)
            return (
              <Button
                key={q.days}
                type="button"
                size="sm"
                variant={value === date ? 'default' : 'outline'}
                onClick={() => onChange(date)}
              >
                {q.label}
              </Button>
            )
          })}
        </div>
      )}
    </FormField>
  )
}

/* ------------------------------------------------------------------ snapshot */

const snapshotSchema = z.object({
  customerName: requiredText(200),
  customerPhone: phoneSchema,
  device: deviceSchema,
})

/** Corrects the customer/device details stored on THIS order. The customer and device records are not touched. */
export function SnapshotDialog({ order, open, onOpenChange, onDone }: OrderDialogProps) {
  return (
    <OrderDialog open={open} onOpenChange={onOpenChange} title={t.order.snapshotTitle} description={t.order.snapshotHint} wide>
      <SnapshotForm order={order} onClose={() => onOpenChange(false)} onDone={onDone} />
    </OrderDialog>
  )
}

function SnapshotForm({ order, onClose, onDone }: { order: OrderDetails; onClose: () => void; onDone: (m: string) => void }) {
  const correct = useCorrectSnapshot(order.id)
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<z.infer<typeof snapshotSchema>>({
    resolver: zodResolver(snapshotSchema),
    defaultValues: {
      customerName: order.customerName,
      customerPhone: order.customerPhone,
      device: {
        type: order.deviceType,
        brand: order.deviceBrand,
        model: order.deviceModel,
        serialNumber: order.deviceSerialNumber ?? '',
        imei: order.deviceImei ?? '',
        description: '',
      },
    },
  })
  const { errors, isSubmitting } = form.formState

  async function onSubmit(values: z.infer<typeof snapshotSchema>) {
    setFormError(null)
    const device = toDeviceInput(values.device)
    try {
      await correct.mutateAsync({
        customerName: values.customerName,
        customerPhone: values.customerPhone,
        deviceType: device.type,
        deviceBrand: device.brand,
        deviceModel: device.model,
        deviceSerialNumber: device.serialNumber,
        deviceImei: device.imei,
        version: order.version,
      })
      onDone(t.order.done.saved)
      onClose()
    } catch (error) {
      setFormError(orderErrorMessage(error))
    }
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-4">
      {formError && <FormAlert>{formError}</FormAlert>}
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField label={t.customers.form.fullName} error={errors.customerName?.message}>
          {(p) => <Input {...p} {...form.register('customerName')} />}
        </FormField>
        <FormField label={t.customers.form.phone} error={errors.customerPhone?.message}>
          {(p) => <Input {...p} type="tel" {...form.register('customerPhone')} />}
        </FormField>
      </div>
      <DeviceFields form={form} name="device" showDescription={false} />
      <SaveFooter onCancel={onClose} saving={isSubmitting} />
    </form>
  )
}

function SaveFooter({ onCancel, saving }: { onCancel: () => void; saving: boolean }) {
  return (
    <DialogFooter>
      <Button type="button" variant="outline" onClick={onCancel}>
        {t.common.cancel}
      </Button>
      <Button type="submit" disabled={saving}>
        {saving ? t.common.saving : t.common.save}
      </Button>
    </DialogFooter>
  )
}
