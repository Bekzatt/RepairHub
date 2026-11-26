import { cn } from '@/lib/utils'
import { ru } from '@/locales/ru'

interface Stage {
  name: string
  hint: string
  /** The order is waiting on the customer rather than on the shop. */
  waitsForCustomer?: boolean
}

// Display-only outline of the planned workflow. The real status model and
// allowed transitions will be defined in the Domain layer with the orders module.
const stages: Stage[] = [
  { name: 'Приём', hint: 'Устройство принято, выдана квитанция' },
  { name: 'Диагностика', hint: 'Мастер ищет неисправность' },
  { name: 'Согласование', hint: 'Клиент подтверждает стоимость', waitsForCustomer: true },
  { name: 'Ремонт', hint: 'Работы и замена запчастей' },
  { name: 'Готов', hint: 'Клиенту отправлено уведомление', waitsForCustomer: true },
  { name: 'Выдан', hint: 'Оплата и гарантийный талон' },
]

export function RepairTrack() {
  return (
    <section aria-labelledby="track-title" className="rounded-xl border bg-card">
      <header className="flex flex-col gap-1 px-5 pt-5 sm:px-6">
        <h2 id="track-title" className="text-[15px] font-semibold">
          {ru.dashboard.track.title}
        </h2>
        <p className="max-w-prose text-sm text-muted-foreground">{ru.dashboard.track.description}</p>
      </header>

      <ol className="grid gap-px px-5 pt-5 pb-5 sm:px-6 lg:grid-cols-6 lg:gap-0">
        {stages.map((stage, index) => (
          <li key={stage.name} className="relative flex gap-4 pb-5 last:pb-0 lg:flex-col lg:gap-3 lg:pr-4 lg:pb-0">
            {/* Rail: vertical on small screens, horizontal on large screens */}
            <div className="relative flex flex-col items-center lg:flex-row">
              <span
                className={cn(
                  'z-10 flex size-7 shrink-0 items-center justify-center rounded-full border-2 bg-card text-xs font-semibold tabular-nums',
                  stage.waitsForCustomer ? 'border-warning text-warning' : 'border-primary text-primary',
                )}
              >
                {index + 1}
              </span>
              {index < stages.length - 1 && (
                <span
                  aria-hidden
                  className="absolute top-7 bottom-[-20px] left-1/2 w-px -translate-x-1/2 bg-border lg:top-1/2 lg:right-[-16px] lg:bottom-auto lg:left-7 lg:h-px lg:w-auto lg:translate-x-0"
                />
              )}
            </div>

            <div className="min-w-0 flex-1">
              <div className="flex items-baseline justify-between gap-3 lg:block">
                <h3 className="text-sm font-semibold">{stage.name}</h3>
                <p className="text-lg font-semibold tabular-nums text-muted-foreground/70 lg:mt-2 lg:text-2xl">
                  0
                  <span className="sr-only"> {ru.dashboard.track.orders}</span>
                </p>
              </div>
              <p className="mt-0.5 text-xs leading-relaxed text-muted-foreground lg:mt-1">{stage.hint}</p>
              {stage.waitsForCustomer && (
                <span className="mt-2 inline-block rounded-full bg-warning-soft px-2 py-0.5 text-[11px] font-medium text-warning">
                  {ru.dashboard.track.waiting}
                </span>
              )}
            </div>
          </li>
        ))}
      </ol>
    </section>
  )
}
