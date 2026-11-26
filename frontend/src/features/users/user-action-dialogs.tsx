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
import { ru } from '@/locales/ru'
import { useResetPassword, useSetUserActive } from './api'
import type { User } from './types'

interface ActionDialogProps {
  user: User | null
  onClose: () => void
  onDone: (message: string) => void
}

// The inner components are mounted only while their dialog is open, so their state starts fresh each time.

export function ResetPasswordDialog({ user, onClose, onDone }: ActionDialogProps) {
  return (
    <Dialog open={user !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{ru.users.resetDialog.title}</DialogTitle>
          <DialogDescription>{user && ru.users.resetDialog.description(user.fullName)}</DialogDescription>
        </DialogHeader>
        {user && <ResetPasswordForm user={user} onClose={onClose} onDone={onDone} />}
      </DialogContent>
    </Dialog>
  )
}

const resetSchema = z.object({ newPassword: passwordSchema })

function ResetPasswordForm({ user, onClose, onDone }: ActionDialogProps & { user: User }) {
  const resetPassword = useResetPassword()
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<z.infer<typeof resetSchema>>({ resolver: zodResolver(resetSchema), defaultValues: { newPassword: '' } })

  const onSubmit = form.handleSubmit(async ({ newPassword }) => {
    try {
      await resetPassword.mutateAsync({ id: user.id, newPassword })
      onDone(ru.users.done.passwordReset)
      onClose()
    } catch (error) {
      if (!applyFieldErrors(error, form.setError, ['newPassword'], { newPassword: ru.validation.passwordLength }))
        setFormError(errorMessage(error))
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate className="space-y-4">
      {formError && <FormAlert>{formError}</FormAlert>}
      <FormField label={ru.changePassword.next} error={form.formState.errors.newPassword?.message} hint={ru.users.form.passwordHint}>
        {(field) => <Input {...field} type="text" autoComplete="new-password" {...form.register('newPassword')} />}
      </FormField>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onClose}>
          {ru.users.form.cancel}
        </Button>
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {ru.users.resetDialog.submit}
        </Button>
      </DialogFooter>
    </form>
  )
}

export function DeactivateDialog({ user, onClose, onDone }: ActionDialogProps) {
  return (
    <Dialog open={user !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{ru.users.deactivateDialog.title}</DialogTitle>
          <DialogDescription>{user && ru.users.deactivateDialog.description(user.fullName)}</DialogDescription>
        </DialogHeader>
        {user && <DeactivateConfirm user={user} onClose={onClose} onDone={onDone} />}
      </DialogContent>
    </Dialog>
  )
}

function DeactivateConfirm({ user, onClose, onDone }: ActionDialogProps & { user: User }) {
  const setActive = useSetUserActive()
  const [error, setError] = useState<string | null>(null)

  async function confirm() {
    try {
      await setActive.mutateAsync({ id: user.id, active: false })
      onDone(ru.users.done.deactivated)
      onClose()
    } catch (e) {
      setError(errorMessage(e))
    }
  }

  return (
    <>
      {error && <FormAlert>{error}</FormAlert>}
      <DialogFooter>
        <Button variant="outline" onClick={onClose}>
          {ru.users.form.cancel}
        </Button>
        <Button variant="destructive" onClick={() => void confirm()} disabled={setActive.isPending}>
          {ru.users.deactivateDialog.submit}
        </Button>
      </DialogFooter>
    </>
  )
}
