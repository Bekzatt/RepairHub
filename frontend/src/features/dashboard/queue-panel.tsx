import { Wrench } from 'lucide-react'
import { ru } from '@/locales/ru'

export function QueuePanel() {
  return (
    <section aria-labelledby="queue-title" className="flex flex-col rounded-xl border bg-card">
      <h2 id="queue-title" className="px-5 pt-5 text-[15px] font-semibold sm:px-6">
        {ru.dashboard.queue.title}
      </h2>
      <div className="flex flex-1 flex-col items-center justify-center px-6 py-12 text-center">
        <div className="mb-4 flex size-12 items-center justify-center rounded-full bg-accent text-accent-foreground">
          <Wrench className="size-5" strokeWidth={1.75} />
        </div>
        <p className="text-sm font-medium">{ru.dashboard.queue.emptyTitle}</p>
        <p className="mt-1 max-w-sm text-sm text-muted-foreground">{ru.dashboard.queue.emptyText}</p>
      </div>
    </section>
  )
}
