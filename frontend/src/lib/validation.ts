import { z } from 'zod'
import { ru } from '@/locales/ru'

/** Mirrors the server's PasswordPolicy (10–128 characters). The server remains the authority. */
export const passwordSchema = z
  .string()
  .min(10, { error: ru.validation.passwordLength })
  .max(128, { error: ru.validation.passwordLength })
  .refine((value) => value.trim().length > 0, { error: ru.validation.passwordLength })

export const emailSchema = z.email({ error: ru.auth.emailInvalid }).max(254, { error: ru.validation.tooLong })
