import { useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { FormAlert, FormField } from '@/components/form-field'
import { BrandMark } from '@/components/layout/brand-mark'
import { login } from '@/features/auth/auth-api'
import { useAuth } from '@/features/auth/auth-store'
import { FullScreenLoader } from '@/features/auth/route-guards'
import { ApiError } from '@/lib/api-client'
import { errorMessage } from '@/lib/form-errors'
import { emailSchema } from '@/lib/validation'
import { ru } from '@/locales/ru'

const schema = z.object({
  email: emailSchema,
  password: z.string().min(1, { error: ru.auth.passwordRequired }).max(128),
})

type Values = z.infer<typeof schema>

export function LoginPage() {
  const auth = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { email: '', password: '' } })
  const { errors, isSubmitting } = form.formState

  const returnTo = (location.state as { from?: string } | null)?.from ?? '/'

  if (auth.status === 'loading') return <FullScreenLoader />
  if (auth.status === 'authenticated') return <Navigate to={returnTo} replace />

  const onSubmit = form.handleSubmit(async ({ email, password }) => {
    setFormError(null)
    try {
      await login(email, password)
      navigate(returnTo, { replace: true })
    } catch (error) {
      form.resetField('password')
      if (error instanceof ApiError && error.status === 401) setFormError(ru.auth.invalidCredentials)
      else if (error instanceof ApiError && error.status === 429) setFormError(ru.auth.tooManyAttempts)
      else setFormError(errorMessage(error))
    }
  })

  return (
    <div className="grid min-h-dvh lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]">
      <aside className="relative hidden flex-col justify-between overflow-hidden bg-sidebar p-10 text-sidebar-foreground lg:flex">
        <div className="flex items-center gap-3">
          <BrandMark className="size-9 text-sidebar-primary" />
          <span className="text-lg font-semibold text-white">{ru.app.name}</span>
        </div>
        <div className="max-w-sm">
          <h2 className="text-[28px] leading-tight font-semibold text-white">{ru.auth.brandTitle}</h2>
          <p className="mt-3 text-sm leading-relaxed text-sidebar-foreground/80">{ru.auth.brandText}</p>
        </div>
        <p className="text-xs text-sidebar-muted">{ru.app.version}</p>
        {/* Quiet detail: the screw-slot cross from the mark, very large and faint */}
        <svg aria-hidden viewBox="0 0 100 100" className="pointer-events-none absolute -right-24 -bottom-24 size-96 opacity-[0.06]">
          <circle cx="50" cy="50" r="44" fill="none" stroke="currentColor" strokeWidth="6" />
          <path d="M50 22v56M22 50h56" stroke="currentColor" strokeWidth="6" strokeLinecap="round" />
        </svg>
      </aside>

      <main className="flex items-center justify-center px-4 py-12 sm:px-8">
        <div className="w-full max-w-sm">
          <div className="mb-8 flex items-center gap-3 lg:hidden">
            <BrandMark className="size-9 text-primary" />
            <span className="text-lg font-semibold">{ru.app.name}</span>
          </div>

          <h1 className="text-2xl font-semibold tracking-tight">{ru.auth.title}</h1>
          <p className="mt-1.5 text-sm text-muted-foreground">{ru.auth.subtitle}</p>

          <form onSubmit={onSubmit} noValidate className="mt-8 space-y-5">
            {formError && <FormAlert>{formError}</FormAlert>}

            <FormField label={ru.auth.email} error={errors.email?.message}>
              {(field) => (
                <Input
                  {...field}
                  type="email"
                  autoComplete="username"
                  inputMode="email"
                  autoFocus
                  className="h-10"
                  {...form.register('email')}
                />
              )}
            </FormField>

            <FormField label={ru.auth.password} error={errors.password?.message}>
              {(field) => (
                <Input
                  {...field}
                  type="password"
                  autoComplete="current-password"
                  className="h-10"
                  {...form.register('password')}
                />
              )}
            </FormField>

            <Button type="submit" className="h-10 w-full" disabled={isSubmitting}>
              {isSubmitting ? ru.auth.submitting : ru.auth.submit}
            </Button>
          </form>

          <p className="mt-8 text-xs leading-relaxed text-muted-foreground">{ru.auth.noAccount}</p>
        </div>
      </main>
    </div>
  )
}
