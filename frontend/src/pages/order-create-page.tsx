import { useEffect, useState, type ReactNode } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { Controller, useForm, type UseFormReturn } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { ArrowLeft, Check, Plus, Search, UserPlus, X } from 'lucide-react'
import { FormAlert, FormField } from '@/components/form-field'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { useCurrentUser } from '@/features/auth/auth-store'
import { phoneMatches, useCustomer, useCustomerDevices, useCustomerLookup, usePhoneMatches } from '@/features/customers/api'
import { DuplicateMatches } from '@/features/customers/customer-form-dialog'
import type { Customer, CustomerMatch, Device } from '@/features/customers/types'
import { DeviceFields, deviceSchema, emptyDevice, toDeviceInput } from '@/features/devices/device-fields'
import { useAssignees, useCreateOrder } from '@/features/repair-orders/api'
import { ExpectedDateField } from '@/features/repair-orders/order-dialogs'
import { errorMessage } from '@/lib/form-errors'
import { formatDate, todayIn } from '@/lib/format'
import { cn } from '@/lib/utils'
import {
  moneySchema,
  optionalEmailSchema,
  optionalPhoneSchema,
  optionalText,
  orNull,
  parseMoney,
  phoneSchema,
  requiredText,
} from '@/lib/validation'
import { t } from '@/locales'

const NO_TECHNICIAN = '__later__'

const newCustomerSchema = z.object({
  fullName: requiredText(200),
  phone: phoneSchema,
  additionalPhone: optionalPhoneSchema,
  email: optionalEmailSchema,
})

const baseSchema = z.object({
  customerMode: z.enum(['none', 'existing', 'new']),
  customerId: z.string(),
  newCustomer: z.object({ fullName: z.string(), phone: z.string(), additionalPhone: z.string(), email: z.string() }),
  deviceMode: z.enum(['existing', 'new']),
  deviceId: z.string(),
  newDevice: z.object({
    type: deviceSchema.shape.type,
    brand: z.string(),
    model: z.string(),
    serialNumber: z.string(),
    imei: z.string(),
    description: z.string(),
  }),
  problem: requiredText(2000),
  condition: optionalText(2000),
  accessories: optionalText(1000),
  technicianId: z.string(),
  expectedCompletionDate: z.string(),
  estimatedPrice: moneySchema,
})
type Values = z.infer<typeof baseSchema>

/** Validates a nested part with its own schema, reporting issues at `path`. */
function check(schema: z.ZodType, value: unknown, path: string, ctx: z.RefinementCtx) {
  const result = schema.safeParse(value)
  if (!result.success)
    for (const issue of result.error.issues)
      ctx.addIssue({ code: 'custom', message: issue.message, path: [path, ...issue.path.map(String)] })
}

function buildSchema(today: string) {
  return baseSchema.superRefine((v, ctx) => {
    if (v.customerMode === 'none') ctx.addIssue({ code: 'custom', path: ['customerId'], message: t.validation.chooseCustomer })
    if (v.customerMode === 'new') check(newCustomerSchema, v.newCustomer, 'newCustomer', ctx)
    if (v.deviceMode === 'existing' && !v.deviceId)
      ctx.addIssue({ code: 'custom', path: ['deviceId'], message: t.validation.chooseDevice })
    if (v.deviceMode === 'new' && v.customerMode !== 'none') check(deviceSchema, v.newDevice, 'newDevice', ctx)
    if (v.expectedCompletionDate && v.expectedCompletionDate < today)
      ctx.addIssue({ code: 'custom', path: ['expectedCompletionDate'], message: t.validation.datePast })
  })
}

/**
 * Order intake on one page, top to bottom: customer → device → problem → technician/date/price.
 * A dedicated page (not a wizard or a dialog): every field stays visible for a final check, it works with the
 * keyboard alone, and it can be opened pre-filled from a customer or device page (?customerId=&deviceId=).
 * Everything — new customer, new device, number, history — is created by ONE request in one transaction.
 */
export function OrderCreatePage() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const { workshop } = useCurrentUser()
  const today = todayIn(workshop.timeZone)
  const createOrder = useCreateOrder()
  const [selectedCustomer, setSelectedCustomer] = useState<Customer | null>(null)
  const [allowDuplicatePhone, setAllowDuplicatePhone] = useState(false)
  const [serverMatches, setServerMatches] = useState<CustomerMatch[] | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  const form = useForm<Values>({
    resolver: zodResolver(buildSchema(today)),
    defaultValues: {
      customerMode: 'none',
      customerId: '',
      newCustomer: { fullName: '', phone: '', additionalPhone: '', email: '' },
      deviceMode: 'new',
      deviceId: '',
      newDevice: emptyDevice,
      problem: '',
      condition: '',
      accessories: '',
      technicianId: NO_TECHNICIAN,
      expectedCompletionDate: '',
      estimatedPrice: '',
    },
  })
  const customerMode = form.watch('customerMode')

  // Opened from a customer/device page: pre-select them.
  const presetCustomer = useCustomer(params.get('customerId') ?? undefined)
  useEffect(() => {
    const customer = presetCustomer.data?.customer
    if (customer && !customer.isArchived && form.getValues('customerMode') === 'none') selectCustomer(customer, params.get('deviceId'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [presetCustomer.data])

  function selectCustomer(customer: Customer, deviceId: string | null = null) {
    setSelectedCustomer(customer)
    setServerMatches(null)
    form.setValue('customerMode', 'existing')
    form.setValue('customerId', customer.id)
    form.setValue('deviceMode', deviceId ? 'existing' : 'new')
    form.setValue('deviceId', deviceId ?? '')
    form.clearErrors(['customerId', 'newCustomer'])
  }

  function startNewCustomer(term: string) {
    const looksLikePhone = /^[\d\s+()-]{3,}$/.test(term.trim())
    setSelectedCustomer(null)
    form.setValue('customerMode', 'new')
    form.setValue('customerId', '')
    form.setValue('deviceMode', 'new')
    form.setValue('deviceId', '')
    form.setValue(looksLikePhone ? 'newCustomer.phone' : 'newCustomer.fullName', term.trim())
    form.clearErrors('customerId')
  }

  function resetCustomer() {
    setSelectedCustomer(null)
    setAllowDuplicatePhone(false)
    setServerMatches(null)
    form.setValue('customerMode', 'none')
    form.setValue('customerId', '')
    form.setValue('deviceMode', 'new')
    form.setValue('deviceId', '')
  }

  async function onSubmit(v: Values) {
    setFormError(null)
    const isNewCustomer = v.customerMode === 'new'
    try {
      const order = await createOrder.mutateAsync({
        customerId: isNewCustomer ? null : v.customerId,
        newCustomer: isNewCustomer
          ? {
              fullName: v.newCustomer.fullName.trim(),
              phone: v.newCustomer.phone.trim(),
              additionalPhone: orNull(v.newCustomer.additionalPhone),
              email: orNull(v.newCustomer.email),
              notes: null,
              allowDuplicatePhone,
            }
          : null,
        deviceId: v.deviceMode === 'existing' ? v.deviceId : null,
        newDevice: v.deviceMode === 'new' ? toDeviceInput(v.newDevice) : null,
        problem: v.problem.trim(),
        condition: orNull(v.condition),
        accessories: orNull(v.accessories),
        assignedTechnicianId: v.technicianId === NO_TECHNICIAN ? null : v.technicianId,
        expectedCompletionDate: orNull(v.expectedCompletionDate),
        estimatedPrice: parseMoney(v.estimatedPrice),
      })
      navigate(`/orders/${order.id}`, { state: { created: t.order.done.created(order.number) } })
    } catch (error) {
      const matches = phoneMatches(error)
      if (matches) {
        setServerMatches(matches)
        document.getElementById('section-customer')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
      } else {
        setFormError(errorMessage(error))
      }
    }
  }

  const { errors, isSubmitting } = form.formState

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <div>
        <Link to="/orders" className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" />
          {t.order.back}
        </Link>
        <h1 className="mt-3 text-2xl font-semibold tracking-tight sm:text-[28px]">{t.newOrder.title}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{t.newOrder.description}</p>
      </div>

      <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-5">
        {formError && <FormAlert>{formError}</FormAlert>}

        <Section id="section-customer" step={1} title={t.newOrder.sections.customer}>
          {customerMode === 'existing' && selectedCustomer ? (
            <SelectedCustomer customer={selectedCustomer} onChange={resetCustomer} />
          ) : customerMode === 'new' ? (
            <NewCustomerFields
              form={form}
              allowDuplicate={allowDuplicatePhone}
              onAllowDuplicate={() => setAllowDuplicatePhone(true)}
              serverMatches={serverMatches}
              onUse={(match) => selectCustomer({ ...match, email: null, notes: null, createdAt: '', updatedAt: '' })}
              onCancel={resetCustomer}
            />
          ) : (
            <CustomerSearch onSelect={(c) => selectCustomer(c)} onNew={startNewCustomer} error={errors.customerId?.message} />
          )}
        </Section>

        <Section step={2} title={t.newOrder.sections.device} disabled={customerMode === 'none'}>
          {customerMode !== 'none' && (
            <DeviceChoice form={form} customerId={customerMode === 'existing' ? (selectedCustomer?.id ?? null) : null} />
          )}
        </Section>

        <Section step={3} title={t.newOrder.sections.problem}>
          <div className="space-y-4">
            <FormField label={t.order.problem} error={errors.problem?.message}>
              {(p) => <Textarea {...p} rows={3} placeholder={t.newOrder.problemPlaceholder} {...form.register('problem')} />}
            </FormField>
            <FormField label={t.order.condition} optionalLabel={t.common.optional} error={errors.condition?.message}>
              {(p) => <Textarea {...p} rows={2} placeholder={t.newOrder.conditionPlaceholder} {...form.register('condition')} />}
            </FormField>
            <AccessoriesField form={form} />
          </div>
        </Section>

        <Section step={4} title={t.newOrder.sections.details}>
          <div className="space-y-4">
            <TechnicianField form={form} />
            <ExpectedDateField
              value={form.watch('expectedCompletionDate')}
              onChange={(v) => form.setValue('expectedCompletionDate', v, { shouldValidate: true })}
              today={today}
              error={errors.expectedCompletionDate?.message}
              register={form.register('expectedCompletionDate')}
            />
            <FormField
              label={`${t.order.estimatedPrice}, ${workshop.currency}`}
              optionalLabel={t.common.optional}
              error={errors.estimatedPrice?.message}
            >
              {(p) => <Input {...p} inputMode="decimal" className="sm:w-56" {...form.register('estimatedPrice')} />}
            </FormField>
          </div>
        </Section>

        <div className="flex flex-col-reverse gap-3 border-t pt-5 sm:flex-row sm:justify-end">
          <Button asChild type="button" variant="outline">
            <Link to="/orders">{t.common.cancel}</Link>
          </Button>
          <Button type="submit" size="lg" disabled={isSubmitting}>
            {isSubmitting ? t.newOrder.submitting : t.newOrder.submit}
          </Button>
        </div>
      </form>
    </div>
  )
}

function Section({
  id,
  step,
  title,
  disabled,
  children,
}: {
  id?: string
  step: number
  title: string
  disabled?: boolean
  children: ReactNode
}) {
  return (
    <section id={id} className={cn('scroll-mt-20 rounded-xl border bg-card p-5', disabled && 'opacity-60')}>
      <h2 className="mb-4 flex items-center gap-3 text-[15px] font-semibold">
        <span
          aria-hidden
          className="flex size-6 items-center justify-center rounded-full bg-accent text-xs font-semibold text-accent-foreground tabular-nums"
        >
          {step}
        </span>
        {title}
      </h2>
      {children}
    </section>
  )
}

function CustomerSearch({
  onSelect,
  onNew,
  error,
}: {
  onSelect: (customer: Customer) => void
  onNew: (term: string) => void
  error?: string
}) {
  const [term, setTerm] = useState('')
  const [debounced, setDebounced] = useState('')
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(term), 250)
    return () => clearTimeout(timer)
  }, [term])
  const lookup = useCustomerLookup(debounced)
  const searching = debounced.trim().length >= 2
  const results = searching ? lookup.data?.items : undefined

  return (
    <div className="space-y-3">
      <FormField label={t.newOrder.customerSearch} hint={!error ? t.newOrder.customerSearchHint : undefined} error={error}>
        {(p) => (
          <div className="relative">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input {...p} value={term} onChange={(e) => setTerm(e.target.value)} autoFocus autoComplete="off" className="pl-9" />
          </div>
        )}
      </FormField>

      {searching && (
        <div className="rounded-lg border">
          {lookup.isFetching && !results ? (
            <p className="px-3 py-3 text-sm text-muted-foreground">{t.newOrder.searching}</p>
          ) : results && results.length > 0 ? (
            <ul className="divide-y">
              {results.map((customer) => (
                <li key={customer.id}>
                  <button
                    type="button"
                    onClick={() => onSelect(customer)}
                    className="flex w-full items-center justify-between gap-3 px-3 py-2.5 text-left hover:bg-muted/60 focus-visible:bg-muted/60 focus-visible:outline-none"
                  >
                    <span className="min-w-0">
                      <span className="block truncate text-sm font-medium">{customer.fullName}</span>
                      <span className="block text-xs text-muted-foreground tabular-nums">
                        {[customer.phone, customer.additionalPhone].filter(Boolean).join(' · ')}
                      </span>
                    </span>
                    <Check className="size-4 text-muted-foreground" />
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <p className="px-3 py-3 text-sm text-muted-foreground">{t.newOrder.noMatches}</p>
          )}
        </div>
      )}

      <Button type="button" variant="outline" onClick={() => onNew(term)}>
        <UserPlus />
        {t.newOrder.newCustomer}
      </Button>
    </div>
  )
}

function SelectedCustomer({ customer, onChange }: { customer: Customer; onChange: () => void }) {
  return (
    <div className="flex items-center justify-between gap-3 rounded-lg border border-primary/40 bg-accent/50 px-4 py-3">
      <div className="min-w-0">
        <p className="truncate font-medium">{customer.fullName}</p>
        <p className="text-sm text-muted-foreground tabular-nums">
          {[customer.phone, customer.additionalPhone].filter(Boolean).join(' · ')}
        </p>
      </div>
      <Button type="button" variant="ghost" size="sm" onClick={onChange}>
        <X />
        {t.newOrder.changeCustomer}
      </Button>
    </div>
  )
}

function NewCustomerFields({
  form,
  allowDuplicate,
  onAllowDuplicate,
  serverMatches,
  onUse,
  onCancel,
}: {
  form: UseFormReturn<Values>
  allowDuplicate: boolean
  onAllowDuplicate: () => void
  serverMatches: CustomerMatch[] | null
  onUse: (match: CustomerMatch) => void
  onCancel: () => void
}) {
  const errors = form.formState.errors.newCustomer
  const phone = form.watch('newCustomer.phone')
  // Warn while typing; the server checks again on submit (and returns the matches if it finds any).
  const lookup = usePhoneMatches(phone, !allowDuplicate)
  const liveMatches: CustomerMatch[] | undefined = lookup.data?.items.map((c) => ({
    id: c.id,
    fullName: c.fullName,
    phone: c.phone,
    additionalPhone: c.additionalPhone,
    isArchived: c.isArchived,
  }))
  const matches = serverMatches ?? (liveMatches && liveMatches.length > 0 ? liveMatches : null)

  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField label={t.customers.form.fullName} error={errors?.fullName?.message}>
          {(p) => <Input {...p} autoComplete="off" {...form.register('newCustomer.fullName')} />}
        </FormField>
        <FormField label={t.customers.form.phone} error={errors?.phone?.message}>
          {(p) => <Input {...p} type="tel" inputMode="tel" autoComplete="off" {...form.register('newCustomer.phone')} />}
        </FormField>
        <FormField label={t.customers.form.additionalPhone} optionalLabel={t.common.optional} error={errors?.additionalPhone?.message}>
          {(p) => <Input {...p} type="tel" inputMode="tel" autoComplete="off" {...form.register('newCustomer.additionalPhone')} />}
        </FormField>
        <FormField label={t.customers.form.email} optionalLabel={t.common.optional} error={errors?.email?.message}>
          {(p) => <Input {...p} type="email" autoComplete="off" {...form.register('newCustomer.email')} />}
        </FormField>
      </div>

      {allowDuplicate ? (
        <p className="text-sm text-muted-foreground">{t.newOrder.duplicateConfirmed}</p>
      ) : (
        matches && (
          <div className="space-y-2">
            <DuplicateMatches matches={matches} onUse={onUse} />
            <Button type="button" variant="outline" size="sm" onClick={onAllowDuplicate}>
              {t.newOrder.confirmDuplicate}
            </Button>
          </div>
        )
      )}

      <Button type="button" variant="ghost" size="sm" onClick={onCancel} className="-ml-2 text-muted-foreground">
        <Search />
        {t.newOrder.changeCustomer}
      </Button>
    </div>
  )
}

function DeviceChoice({ form, customerId }: { form: UseFormReturn<Values>; customerId: string | null }) {
  const devices = useCustomerDevices(customerId ?? undefined)
  const { workshop } = useCurrentUser()
  const deviceMode = form.watch('deviceMode')
  const deviceId = form.watch('deviceId')
  const list: Device[] = customerId ? (devices.data ?? []) : []

  function choose(id: string | null) {
    form.setValue('deviceMode', id ? 'existing' : 'new')
    form.setValue('deviceId', id ?? '')
    form.clearErrors(['deviceId', 'newDevice'])
  }

  return (
    <div className="space-y-4">
      {customerId && (
        <div>
          <p className="mb-2 text-sm font-medium">{t.newOrder.devicesOfCustomer}</p>
          {devices.isLoading ? (
            <span className="block h-14 animate-pulse rounded-lg bg-muted" />
          ) : (
            <div className="grid gap-2 sm:grid-cols-2" role="radiogroup" aria-label={t.newOrder.devicesOfCustomer}>
              {list.map((device) => (
                <ChoiceCard key={device.id} selected={deviceMode === 'existing' && deviceId === device.id} onSelect={() => choose(device.id)}>
                  <span className="block truncate font-medium">
                    {device.brand} {device.model}
                  </span>
                  <span className="block truncate text-xs text-muted-foreground">
                    {t.deviceTypes[device.type]}
                    {device.serialNumber && ` · ${device.serialNumber}`}
                  </span>
                  <span className="block text-xs text-muted-foreground">
                    {device.lastReceivedAt
                      ? t.newOrder.lastRepair(formatDate(device.lastReceivedAt, workshop.timeZone))
                      : t.newOrder.neverRepaired}
                  </span>
                </ChoiceCard>
              ))}
              <ChoiceCard selected={deviceMode === 'new'} onSelect={() => choose(null)}>
                <span className="flex items-center gap-2 font-medium">
                  <Plus className="size-4" />
                  {t.newOrder.newDevice}
                </span>
              </ChoiceCard>
            </div>
          )}
          {form.formState.errors.deviceId && (
            <p className="mt-2 text-xs text-destructive">{form.formState.errors.deviceId.message}</p>
          )}
        </div>
      )}

      {deviceMode === 'new' && <DeviceFields form={form} name="newDevice" showDescription={false} />}
    </div>
  )
}

function ChoiceCard({ selected, onSelect, children }: { selected: boolean; onSelect: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={selected}
      onClick={onSelect}
      className={cn(
        'min-w-0 rounded-lg border px-3 py-2.5 text-left text-sm transition-colors focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none',
        selected ? 'border-primary bg-accent' : 'hover:bg-muted/50',
      )}
    >
      {children}
    </button>
  )
}

function AccessoriesField({ form }: { form: UseFormReturn<Values> }) {
  const value = form.watch('accessories')
  const present = value.split(',').map((s) => s.trim().toLowerCase())

  function add(item: string) {
    if (present.includes(item.toLowerCase())) return
    const next = value.trim() ? `${value.trim().replace(/,$/, '')}, ${item}` : item
    form.setValue('accessories', next, { shouldDirty: true })
  }

  return (
    <FormField label={t.order.accessories} optionalLabel={t.common.optional} error={form.formState.errors.accessories?.message}>
      {(p) => (
        <div className="space-y-2">
          <Input {...p} placeholder={t.newOrder.accessoriesPlaceholder} {...form.register('accessories')} />
          <div className="flex flex-wrap gap-1.5">
            {t.newOrder.quickAccessories.map((item) => (
              <button
                key={item}
                type="button"
                onClick={() => add(item)}
                disabled={present.includes(item.toLowerCase())}
                className="rounded-full border px-2.5 py-1 text-xs text-muted-foreground transition-colors hover:border-primary hover:text-foreground disabled:opacity-40"
              >
                + {item}
              </button>
            ))}
          </div>
        </div>
      )}
    </FormField>
  )
}

function TechnicianField({ form }: { form: UseFormReturn<Values> }) {
  const assignees = useAssignees()
  return (
    <FormField label={t.order.sections.technician} optionalLabel={t.common.optional}>
      {(p) => (
        <Controller
          control={form.control}
          name="technicianId"
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger {...p} className="w-full sm:w-72">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NO_TECHNICIAN}>{t.newOrder.technicianLater}</SelectItem>
                {assignees.data?.map((a) => (
                  <SelectItem key={a.id} value={a.id}>
                    {a.fullName} · {t.roles[a.role]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      )}
    </FormField>
  )
}
