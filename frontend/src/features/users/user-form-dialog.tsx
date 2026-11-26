import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
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
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { FormAlert, FormField } from '@/components/form-field'
import { useCurrentUser } from '@/features/auth/auth-store'
import { USER_ROLES, type UserRole } from '@/features/auth/types'
import { applyFieldErrors, errorMessage } from '@/lib/form-errors'
import { emailSchema, passwordSchema } from '@/lib/validation'
import { ru } from '@/locales/ru'
import { useCreateUser, useUpdateUser } from './api'
import { assignableRoles } from './permissions'
import type { User } from './types'

const baseSchema = z.object({
  fullName: z.string().trim().min(1, { error: ru.validation.required }).max(200, { error: ru.validation.tooLong }),
  phone: z.string().trim().max(32, { error: ru.validation.tooLong }),
  role: z.enum(USER_ROLES, { error: ru.validation.required }),
})
const createSchema = baseSchema.extend({ email: emailSchema, password: passwordSchema })
// Email and password are set only when creating; on edit they are carried but not validated.
const editSchema = baseSchema.extend({ email: z.string(), password: z.string() })
type Values = z.infer<typeof createSchema>

interface UserFormDialogProps {
  /** null = create a new user */
  user: User | null
  open: boolean
  onOpenChange: (open: boolean) => void
  onDone: (message: string) => void
}

export function UserFormDialog({ user, open, onOpenChange, onDone }: UserFormDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{user ? ru.users.form.editTitle : ru.users.form.createTitle}</DialogTitle>
          {user && <DialogDescription>{user.email}</DialogDescription>}
        </DialogHeader>
        {/* Mounted only while open, so every opening starts from fresh form state. */}
        <UserForm user={user} onClose={() => onOpenChange(false)} onDone={onDone} />
      </DialogContent>
    </Dialog>
  )
}

function UserForm({ user, onClose, onDone }: { user: User | null; onClose: () => void; onDone: (m: string) => void }) {
  const me = useCurrentUser()
  const isSelf = user?.id === me.id
  const roles = assignableRoles(me.role)
  const createUser = useCreateUser()
  const updateUser = useUpdateUser()
  const [formError, setFormError] = useState<string | null>(null)

  const form = useForm<Values>({
    resolver: zodResolver(user ? editSchema : createSchema),
    defaultValues: user
      ? { fullName: user.fullName, phone: user.phone ?? '', role: user.role, email: user.email, password: '' }
      : { fullName: '', email: '', phone: '', role: roles.includes('Technician') ? 'Technician' : roles[0], password: '' },
  })
  const { errors, isSubmitting } = form.formState

  // Your own role is shown but locked; it may be one you could not assign to others.
  const roleOptions: UserRole[] = user && !roles.includes(user.role) ? [user.role, ...roles] : roles

  const onSubmit = form.handleSubmit(async (values) => {
    setFormError(null)
    const phone = values.phone.trim() || null
    try {
      if (user) {
        await updateUser.mutateAsync({ id: user.id, fullName: values.fullName, phone, role: values.role })
        onDone(ru.users.done.updated)
      } else {
        await createUser.mutateAsync({ ...values, phone })
        onDone(ru.users.done.created)
      }
      onClose()
    } catch (error) {
      const handled = applyFieldErrors(error, form.setError, ['fullName', 'email', 'phone', 'role', 'password'], {
        password: ru.validation.passwordLength,
      })
      if (!handled) setFormError(errorMessage(error))
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate className="space-y-4">
      {formError && <FormAlert>{formError}</FormAlert>}

      <FormField label={ru.users.form.fullName} error={errors.fullName?.message}>
        {(field) => <Input {...field} autoComplete="off" {...form.register('fullName')} />}
      </FormField>

      {!user && (
        <FormField label={ru.users.form.email} error={errors.email?.message}>
          {(field) => <Input {...field} type="email" autoComplete="off" {...form.register('email')} />}
        </FormField>
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        <FormField label={ru.users.form.phone} optionalLabel={ru.users.form.phoneOptional} error={errors.phone?.message}>
          {(field) => <Input {...field} type="tel" autoComplete="off" {...form.register('phone')} />}
        </FormField>

        <FormField
          label={ru.users.form.role}
          error={errors.role?.message}
          hint={isSelf ? ru.users.form.ownRoleLocked : undefined}
        >
          {(field) => (
            <Controller
              control={form.control}
              name="role"
              render={({ field: { value, onChange } }) => (
                <Select value={value} onValueChange={onChange} disabled={isSelf}>
                  <SelectTrigger {...field} className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {roleOptions.map((role) => (
                      <SelectItem key={role} value={role}>
                        {ru.roles[role]}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          )}
        </FormField>
      </div>

      {!user && (
        <FormField label={ru.users.form.password} error={errors.password?.message} hint={ru.users.form.passwordHint}>
          {(field) => <Input {...field} type="text" autoComplete="new-password" {...form.register('password')} />}
        </FormField>
      )}

      <DialogFooter>
        <Button type="button" variant="outline" onClick={onClose}>
          {ru.users.form.cancel}
        </Button>
        <Button type="submit" disabled={isSubmitting}>
          {user ? ru.users.form.save : ru.users.form.create}
        </Button>
      </DialogFooter>
    </form>
  )
}
