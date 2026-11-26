import { locale } from '@/locales'

/**
 * Numbers, money and numeric dates use Russian formatting (dd.mm.yyyy, "15 000 ₸"), which is also the
 * convention in Kazakhstan. Month and weekday NAMES are shown only in the Russian UI: Kazakh locale data is
 * missing or incomplete in many browsers (e.g. "M11 26"), so the Kazakh UI uses numeric dates instead.
 */
const LOCALE = 'ru-RU'
const NAMED_DATES = locale === 'ru'

/** Fallback time zone; the real one comes from the signed-in user's workshop. */
export const DEFAULT_TIME_ZONE = 'Asia/Almaty'

/** Timestamps arrive in UTC and are shown in the workshop's time zone. */
export function formatLongDate(date: Date, timeZone = DEFAULT_TIME_ZONE): string {
  if (!NAMED_DATES) return formatDate(date, timeZone)
  const text = new Intl.DateTimeFormat(LOCALE, { timeZone, weekday: 'long', day: 'numeric', month: 'long' }).format(date)
  return text.charAt(0).toUpperCase() + text.slice(1)
}

export function formatTime(date: Date | number, timeZone = DEFAULT_TIME_ZONE): string {
  return new Intl.DateTimeFormat(LOCALE, { timeZone, hour: '2-digit', minute: '2-digit', second: '2-digit' }).format(date)
}

export function initials(fullName: string): string {
  return fullName
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('')
}

/** Short date + time of an instant, e.g. "26.11.2026, 14:05", in the workshop's time zone. */
export function formatDateTime(value: string | Date, timeZone = DEFAULT_TIME_ZONE): string {
  return new Intl.DateTimeFormat(LOCALE, {
    timeZone,
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value))
}

/** Date of an instant in the workshop's time zone, e.g. "26.11.2026". */
export function formatDate(value: string | Date, timeZone = DEFAULT_TIME_ZONE): string {
  return new Intl.DateTimeFormat(LOCALE, { timeZone, day: '2-digit', month: '2-digit', year: 'numeric' }).format(new Date(value))
}

/**
 * A calendar date without time ("2026-12-01", e.g. the expected completion date). It is not an instant,
 * so it is formatted as-is (in UTC) and never shifted by a time zone.
 */
export function formatCalendarDate(isoDate: string, options: Intl.DateTimeFormatOptions = {}): string {
  const format: Intl.DateTimeFormatOptions = NAMED_DATES
    ? { day: 'numeric', month: 'short', ...options }
    : { day: '2-digit', month: '2-digit', year: options.year }
  return new Intl.DateTimeFormat(LOCALE, { timeZone: 'UTC', ...format }).format(
    new Date(`${isoDate}T00:00:00Z`),
  )
}

/** Today's calendar date in the workshop's time zone as "YYYY-MM-DD". */
export function todayIn(timeZone = DEFAULT_TIME_ZONE): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
}

export function addDays(isoDate: string, days: number): string {
  const date = new Date(`${isoDate}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

/** Money in the workshop currency, e.g. "15 000 ₸". Kopecks/tiyn are shown only when present. */
export function formatMoney(amount: number, currency: string): string {
  return new Intl.NumberFormat(LOCALE, { style: 'currency', currency, minimumFractionDigits: 0, maximumFractionDigits: 2 }).format(amount)
}
