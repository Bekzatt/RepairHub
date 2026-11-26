import { useState } from 'react'
import { Link } from 'react-router'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { FormAlert, FormField } from '@/components/form-field'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { errorMessage } from '@/lib/form-errors'
import { optionalEmailSchema, optionalPhoneSchema, optionalText, orNull, phoneSchema, requiredText } from '@/lib/validation'
import { t } from '@/locales'
import { phoneMatches, useCreateCustomer, useUpdateCustomer } from './api'
import type { Customer, CustomerInput, CustomerMatch } from './types'

const schema = z.object({
  fullName: requiredText(200),
  phone: phoneSchema,
  additionalPhone: optionalPhoneSchema,
  email: optionalEmailSchema,
  notes: optionalText(2000),
})
type Values = z.infer<typeof schema>

interface CustomerFormDialogProps {
  /** null = create */
  customer: Customer | null
  open: boolean
  onOpenChange: (open: boolean) => void
  onSaved: (customer: Customer, created: boolean) => void
}

export function CustomerFormDialog({ customer, open, onOpenChange, onSaved }: CustomerFormDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{customer ? t.customers.form.editTitle : t.customers.form.createTitle}</DialogTitle>
        </DialogHeader>
        <CustomerForm customer={customer} onClose={() => onOpenChange(false)} onSaved={onSaved} />
      </DialogContent>
    </Dialog>
  )
}

function CustomerForm({
  customer,
  onClose,
  onSaved,
}: {
  customer: Customer | null
  onClose: () => void
  onSaved: (customer: Customer, created: boolean) => void
}) {
  const create = useCreateCustomer()
  const update = useUpdateCustomer()
  const [matches, setMatches] = useState<CustomerMatch[] | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      fullName: customer?.fullName ?? '',
      phone: customer?.phone ?? '',
      additionalPhone: customer?.additionalPhone ?? '',
      email: customer?.email ?? '',
      notes: customer?.notes ?? '',
    },
  })
  const { errors, isSubmitting } = form.formState

  async function save(values: Values, allowDuplicatePhone: boolean) {
    setFormError(null)
    const input: CustomerInput = {
      fullName: values.fullName,
      phone: values.phone,
      additionalPhone: orNull(values.additionalPhone),
      email: orNull(values.email),
      notes: orNull(values.notes),
      allowDuplicatePhone,
    }
    try {
      const saved = customer ? await update.mutateAsync({ id: customer.id, ...input }) : await create.mutateAsync(input)
      onSaved(saved, !customer)
      onClose()
    } catch (error) {
      const duplicates = phoneMatches(error)
      if (duplicates) setMatches(duplicates)
      else setFormError(errorMessage(error))
    }
  }

  // Changing a phone after a duplicate warning re-checks it.
  const resetMatches = () => matches && setMatches(null)

  return (
    <form onSubmit={form.handleSubmit((v) => save(v, false))} noValidate className="space-y-4">
      {formError && <FormAlert>{formError}</FormAlert>}
      <FormField label={t.customers.form.fullName} error={errors.fullName?.message}>
        {(p) => <Input {...p} autoComplete="off" {...form.register('fullName')} />}
      </FormField>
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField label={t.customers.form.phone} error={errors.phone?.message}>
          {(p) => <Input {...p} type="tel" inputMode="tel" {...form.register('phone', { onChange: resetMatches })} />}
        </FormField>
        <FormField label={t.customers.form.additionalPhone} optionalLabel={t.common.optional} error={errors.additionalPhone?.message}>
          {(p) => <Input {...p} type="tel" inputMode="tel" {...form.register('additionalPhone', { onChange: resetMatches })} />}
        </FormField>
      </div>
      <FormField label={t.customers.form.email} optionalLabel={t.common.optional} error={errors.email?.message}>
        {(p) => <Input {...p} type="email" {...form.register('email')} />}
      </FormField>
      <FormField label={t.customers.form.notes} optionalLabel={t.common.optional} error={errors.notes?.message}>
        {(p) => <Textarea {...p} rows={2} placeholder={t.customers.form.notesPlaceholder} {...form.register('notes')} />}
      </FormField>

      {matches && <DuplicateMatches matches={matches} />}

      <DialogFooter>
        <Button type="button" variant="outline" onClick={onClose}>
          {t.common.cancel}
        </Button>
        {matches ? (
          <Button type="button" disabled={isSubmitting} onClick={form.handleSubmit((v) => save(v, true))}>
            {customer ? t.customers.form.saveAnyway : t.customers.form.createAnyway}
          </Button>
        ) : (
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? t.common.saving : customer ? t.common.save : t.customers.form.create}
          </Button>
        )}
      </DialogFooter>
    </form>
  )
}

/** Existing customers that share the phone number. Never merged automatically. */
export function DuplicateMatches({
  matches,
  onUse,
}: {
  matches: CustomerMatch[]
  /** When set, each match gets a "use this customer" button instead of a link. */
  onUse?: (match: CustomerMatch) => void
}) {
  return (
    <div role="alert" className="rounded-lg border border-warning/40 bg-warning-soft px-3 py-3 text-sm">
      <p className="font-medium text-warning">{t.newOrder.duplicateTitle}</p>
      <p className="mt-0.5 text-muted-foreground">{t.newOrder.duplicateText}</p>
      <ul className="mt-2 space-y-1.5">
        {matches.map((match) => (
          <li key={match.id} className="flex items-center justify-between gap-3 rounded-md bg-card px-3 py-2">
            <span className="min-w-0">
              <span className="block truncate font-medium">
                {match.fullName}
                {match.isArchived && <span className="ml-2 text-xs font-normal text-muted-foreground">({t.common.archived})</span>}
              </span>
              <span className="block text-xs text-muted-foreground tabular-nums">
                {[match.phone, match.additionalPhone].filter(Boolean).join(' · ')}
              </span>
            </span>
            {onUse ? (
              <Button type="button" size="sm" variant="outline" disabled={match.isArchived} onClick={() => onUse(match)}>
                {t.newOrder.useExisting}
              </Button>
            ) : (
              <Button asChild size="sm" variant="outline">
                <Link to={`/customers/${match.id}`}>{t.common.open}</Link>
              </Button>
            )}
          </li>
        ))}
      </ul>
    </div>
  )
}
