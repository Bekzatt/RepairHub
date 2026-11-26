/**
 * UI language. All user-facing text comes from `t`, which is resolved ONCE at startup from the saved choice.
 * Switching the language saves it and reloads the page: simple, and module-level text (Zod schemas,
 * navigation labels) is always in the right language. The session survives the reload via the refresh cookie.
 */
import { kk } from './kk'
import { ru, type Messages } from './ru'

export type { Messages }

export const LOCALES = ['ru', 'kk'] as const
export type Locale = (typeof LOCALES)[number]

const STORAGE_KEY = 'repairhub.locale'

/** Each language's own name, shown in the switcher regardless of the current language. */
export const LOCALE_NAMES: Record<Locale, string> = { ru: 'Русский', kk: 'Қазақша' }

function readSavedLocale(): Locale {
  try {
    const saved = window.localStorage.getItem(STORAGE_KEY)
    return LOCALES.includes(saved as Locale) ? (saved as Locale) : 'ru'
  } catch {
    return 'ru' // storage blocked (private mode, policies)
  }
}

export const locale: Locale = readSavedLocale()

export const t: Messages = locale === 'kk' ? kk : ru

if (typeof document !== 'undefined') document.documentElement.lang = locale

export function setLocale(next: Locale) {
  if (next === locale) return
  try {
    window.localStorage.setItem(STORAGE_KEY, next)
  } catch {
    return // cannot persist the choice; keep the current language
  }
  window.location.reload()
}
