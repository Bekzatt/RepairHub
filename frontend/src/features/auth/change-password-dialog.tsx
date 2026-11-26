import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { FormAlert, FormField } from '@/components/form-field'
import { applyFieldErrors, errorMessage } from '@/lib/form-errors'
import { passwordSchema } from '@/lib/validation'
import { t } from '@/locales'
import { changePassword } from './auth-api'

const schema = z
  .object({
    currentPassword: z.string().min(1, { error: t.validation.required }),
    newPassword: passwordSchema,
    confirmPassword: z.string(),
  })
  .refine((v) => v.newPassword === v.confirmPassword, { path: ['confirmPassword'], error: t.changePassword.mismatch })
  .refine((v) => v.newPassword !== v.currentPassword, { path: ['newPassword'], error: t.changePassword.sameAsCurrent })

type Values = z.infer<typeof schema>

export function ChangePasswordDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const [formError, setFormError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
  })
  const { errors, isSubmitting } = form.formState

  function handleOpenChange(next: boolean) {
    if (!next) {
      form.reset()
      setFormError(null)
      setDone(false)
    }
    onOpenChange(next)
  }

  const onSubmit = form.handleSubmit(async (values) => {
    setFormError(null)
    try {
      await changePassword(values.currentPassword, values.newPassword)
      setDone(true)
    } catch (error) {
      const handled = applyFieldErrors(error, form.setError, ['currentPassword', 'newPassword'], {
        currentPassword: t.changePassword.wrongCurrent,
        newPassword: t.validation.passwordLength,
      })
      if (!handled) setFormError(errorMessage(error))
    }
  })

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{t.changePassword.title}</DialogTitle>
          <DialogDescription>{t.changePassword.description}</DialogDescription>
        </DialogHeader>

        {done ? (
          <>
            <p role="status" className="rounded-md bg-accent px-3 py-2 text-sm text-accent-foreground">
              {t.changePassword.done}
            </p>
            <DialogFooter>
              <Button onClick={() => handleOpenChange(false)}>{t.common.close}</Button>
            </DialogFooter>
          </>
        ) : (
          <form onSubmit={onSubmit} noValidate className="space-y-4">
            {formError && <FormAlert>{formError}</FormAlert>}
            <FormField label={t.changePassword.current} error={errors.currentPassword?.message}>
              {(field) => (
                <Input {...field} type="password" autoComplete="current-password" {...form.register('currentPassword')} />
              )}
            </FormField>
            <FormField label={t.changePassword.next} error={errors.newPassword?.message} hint={t.changePassword.hint}>
              {(field) => <Input {...field} type="password" autoComplete="new-password" {...form.register('newPassword')} />}
            </FormField>
            <FormField label={t.changePassword.confirm} error={errors.confirmPassword?.message}>
              {(field) => (
                <Input {...field} type="password" autoComplete="new-password" {...form.register('confirmPassword')} />
              )}
            </FormField>
            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => handleOpenChange(false)}>
                {t.users.form.cancel}
              </Button>
              <Button type="submit" disabled={isSubmitting}>
                {t.changePassword.submit}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  )
}
