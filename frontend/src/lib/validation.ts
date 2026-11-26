import { z } from 'zod'
import { t } from '@/locales'

/** Mirrors the server's PasswordPolicy (10–128 characters). The server remains the authority. */
export const passwordSchema = z
  .string()
  .min(10, { error: t.validation.passwordLength })
  .max(128, { error: t.validation.passwordLength })
  .refine((value) => value.trim().length > 0, { error: t.validation.passwordLength })

export const emailSchema = z.email({ error: t.auth.emailInvalid }).max(254, { error: t.validation.tooLong })

/** Mirrors the server's PhoneNumber rule: any country format, digits + spaces ( ) - and +, 5–20 digits. */
export function isValidPhone(value: string): boolean {
  if (!/^[\d\s+()-]+$/.test(value)) return false
  const digits = value.replace(/\D/g, '').length
  return digits >= 5 && digits <= 20
}

export const requiredText = (max: number) =>
  z.string().trim().min(1, { error: t.validation.required }).max(max, { error: t.validation.tooLong })

export const optionalText = (max: number) => z.string().trim().max(max, { error: t.validation.tooLong })

export const phoneSchema = z
  .string()
  .trim()
  .min(1, { error: t.validation.required })
  .max(32, { error: t.validation.tooLong })
  .refine(isValidPhone, { error: t.validation.phone })

export const optionalPhoneSchema = z
  .string()
  .trim()
  .max(32, { error: t.validation.tooLong })
  .refine((v) => v === '' || isValidPhone(v), { error: t.validation.phone })

export const optionalEmailSchema = z
  .string()
  .trim()
  .max(254, { error: t.validation.tooLong })
  .refine((v) => v === '' || z.email().safeParse(v).success, { error: t.validation.email })

export const imeiSchema = z
  .string()
  .trim()
  .refine((v) => v === '' || /^\d{14,16}$/.test(v.replace(/[\s-]/g, '')), { error: t.validation.imei })

/** A money amount typed by a person: "15000", "15 000", "15000,50". Empty = not set. */
export const moneySchema = z
  .string()
  .trim()
  .refine((v) => v === '' || /^\d{1,12}([.,]\d{1,2})?$/.test(v.replace(/\s/g, '')), { error: t.validation.money })

export function parseMoney(value: string): number | null {
  const normalized = value.replace(/\s/g, '').replace(',', '.')
  return normalized === '' ? null : Number(normalized)
}

/** Empty string → null, for optional API fields. */
export const orNull = (value: string): string | null => (value.trim() === '' ? null : value.trim())
