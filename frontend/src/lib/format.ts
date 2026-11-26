/** The shop's local time zone. Timestamps are stored in UTC and shown in this zone. */
export const SHOP_TIME_ZONE = 'Asia/Almaty'
const LOCALE = 'ru-RU'

const longDate = new Intl.DateTimeFormat(LOCALE, {
  timeZone: SHOP_TIME_ZONE,
  weekday: 'long',
  day: 'numeric',
  month: 'long',
})

const time = new Intl.DateTimeFormat(LOCALE, {
  timeZone: SHOP_TIME_ZONE,
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
})

export function formatLongDate(date: Date): string {
  const text = longDate.format(date)
  return text.charAt(0).toUpperCase() + text.slice(1)
}

export function formatTime(date: Date | number): string {
  return time.format(date)
}
