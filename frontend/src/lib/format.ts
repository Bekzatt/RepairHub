/** Fallback time zone; the real one comes from the signed-in user's workshop. */
export const DEFAULT_TIME_ZONE = 'Asia/Almaty'
const LOCALE = 'ru-RU'

/** Timestamps arrive in UTC and are shown in the workshop's time zone. */
export function formatLongDate(date: Date, timeZone = DEFAULT_TIME_ZONE): string {
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
