import { cn } from '@/lib/utils'
import { locale, LOCALE_NAMES, LOCALES, setLocale, t } from '@/locales'

/** Two-button language toggle. Each language is labelled in itself, so anyone can find their own. */
export function LanguageSwitcher({ className }: { className?: string }) {
  return (
    <div role="group" aria-label={t.common.language} className={cn('inline-flex rounded-md border bg-card p-0.5', className)}>
      {LOCALES.map((code) => (
        <button
          key={code}
          type="button"
          lang={code}
          aria-pressed={locale === code}
          onClick={() => setLocale(code)}
          className={cn(
            'rounded px-2.5 py-1 text-xs font-medium transition-colors',
            locale === code ? 'bg-sidebar text-white' : 'text-muted-foreground hover:text-foreground',
          )}
        >
          {LOCALE_NAMES[code]}
        </button>
      ))}
    </div>
  )
}
